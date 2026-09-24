using System.Globalization;
using LegacyLens.Core.Analysis;
using LegacyLens.Core.AppCat;
using LegacyLens.Core.Ingestion;
using LegacyLens.Core.Tracing.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.CodeAnalysis.Text;

namespace LegacyLens.Core.Tracing;

/// <summary>
/// TDD Layer 3 — AST Trace. Produces a compiler-verified representation of a method from the Roslyn
/// syntax tree, semantic model and operation tree, so the LLM layers receive resolved types,
/// signatures and call relationships instead of inferring them from raw source text.
/// </summary>
/// <remarks>
/// Implements the trace-based prompting technique of Wytock, Judy and Foster Breilyn (InfoQ, 2025):
/// the model sees fully-qualified names, inheritance, resolved signatures, control flow and
/// cross-project call edges, all established by the compiler before any prompt is built.
/// </remarks>
public sealed class TraceBuilder
{
    public const int DefaultTokenBudget = 6000;

    private const int MaxReportedErrors = 10;

    /// <summary>Matches the <c>symbols</c> table format so traces join back to the baseline index.</summary>
    internal static readonly SymbolDisplayFormat MemberFormat = SymbolDisplayFormat.CSharpErrorMessageFormat;

    private static readonly SymbolDisplayFormat TypeFormat =
        SymbolDisplayFormat.FullyQualifiedFormat.WithGlobalNamespaceStyle(SymbolDisplayGlobalNamespaceStyle.Omitted);

    private readonly Solution _solution;
    private readonly string _solutionDirectory;
    private readonly int _tokenBudget;
    private readonly ILookup<string, AppCatIncident> _incidentsByFile;
    private readonly Dictionary<string, string> _projectsByAssembly = new(StringComparer.OrdinalIgnoreCase);

    /// <param name="solutionDirectory">Base for the relative file paths written into traces.</param>
    public TraceBuilder(
        Solution solution,
        string solutionDirectory,
        AppCatReport? appCat = null,
        int tokenBudget = DefaultTokenBudget)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tokenBudget);

        _solution = solution;
        _solutionDirectory = Path.GetFullPath(solutionDirectory);
        _tokenBudget = tokenBudget;

        // AppCAT reports file paths relative to the analyzed solution's directory.
        _incidentsByFile = (appCat?.Incidents ?? [])
            .Where(i => i.File is not null && i.Line is not null)
            .ToLookup(i => NormalizePath(i.File!), StringComparer.OrdinalIgnoreCase);

        // Project references can surface as metadata references when a design-time build is partial,
        // so solution assemblies are recognized by name as well as by source location.
        foreach (var project in solution.Projects)
            _projectsByAssembly.TryAdd(project.AssemblyName, ProjectGraphLoader.NormalizeProjectName(project.Name));
    }

    /// <summary>The declaration carrying the method's body; partial and abstract declarations have none.</summary>
    public static BaseMethodDeclarationSyntax? FindBodySyntax(IMethodSymbol method, CancellationToken cancellationToken = default) =>
        method.DeclaringSyntaxReferences
            .Select(reference => reference.GetSyntax(cancellationToken))
            .OfType<BaseMethodDeclarationSyntax>()
            .FirstOrDefault(syntax => syntax.Body is not null || syntax.ExpressionBody is not null);

    public async Task<MethodTrace> BuildAsync(
        Project project,
        IMethodSymbol method,
        CancellationToken cancellationToken = default)
    {
        var syntax = FindBodySyntax(method, cancellationToken)
            ?? throw new ArgumentException(
                $"'{method.ToDisplayString(MemberFormat)}' has no source body to trace.", nameof(method));

        var compilation = await project.GetCompilationAsync(cancellationToken)
            ?? throw new InvalidOperationException($"Project '{project.Name}' does not support compilation.");

        var model = compilation.GetSemanticModel(syntax.SyntaxTree);
        var body = model.GetOperation(syntax, cancellationToken);
        var operations = body?.Descendants().ToList() ?? [];

        var text = await syntax.SyntaxTree.GetTextAsync(cancellationToken);
        var (firstLine, lastLine) = MemberLineRange(syntax, text);
        var file = string.IsNullOrEmpty(syntax.SyntaxTree.FilePath) ? null : RelativeFile(syntax.SyntaxTree.FilePath);

        return new MethodTrace
        {
            Id = method.GetDocumentationCommentId() ?? method.ToDisplayString(MemberFormat),
            FullyQualifiedName = method.ToDisplayString(MemberFormat),
            Project = ProjectGraphLoader.NormalizeProjectName(project.Name),
            File = file,
            StartLine = firstLine + 1,
            EndLine = lastLine + 1,
            Complexity = ComplexityScorer.Score(syntax),
            IsObsolete = IsObsolete(method),
            ContainingType = DescribeType(method.ContainingType),
            Signature = DescribeSignature(method),
            Callees = CollectCallees(operations),
            Callers = await CollectCallersAsync(method, cancellationToken),
            State = CollectState(operations),
            DataFlow = AnalyzeDataFlow(model, syntax, method),
            ControlFlow = SummarizeControlFlow(body, cancellationToken),
            Grounding = AssessGrounding(model, syntax, operations, cancellationToken),
            AppCatIncidents = file is null ? [] : IncidentsWithin(file, firstLine + 1, lastLine + 1),
            Chunks = ChunkSource(syntax, text, firstLine, lastLine),
        };
    }

    /// <summary>
    /// 0-based line range of the member, extended upward over the comments directly above it: in
    /// legacy code those comments are often the only written record of why a rule exists.
    /// </summary>
    private static (int First, int Last) MemberLineRange(BaseMethodDeclarationSyntax syntax, SourceText text)
    {
        var start = syntax.SpanStart;
        var trivia = syntax.GetLeadingTrivia();
        for (var i = trivia.Count - 1; i >= 0; i--)
        {
            var item = trivia[i];
            if (item.IsKind(SyntaxKind.WhitespaceTrivia) || item.IsKind(SyntaxKind.EndOfLineTrivia))
                continue;

            if (!IsComment(item))
                break;

            start = item.SpanStart;
        }

        return (
            text.Lines.GetLineFromPosition(start).LineNumber,
            text.Lines.GetLineFromPosition(syntax.Span.End).LineNumber);
    }

    private static bool IsComment(SyntaxTrivia trivia) =>
        trivia.IsKind(SyntaxKind.SingleLineCommentTrivia)
        || trivia.IsKind(SyntaxKind.MultiLineCommentTrivia)
        || trivia.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia)
        || trivia.IsKind(SyntaxKind.MultiLineDocumentationCommentTrivia);

    private IReadOnlyList<TraceChunk> ChunkSource(
        BaseMethodDeclarationSyntax syntax,
        SourceText text,
        int firstLine,
        int lastLine)
    {
        var raw = Enumerable.Range(firstLine, lastLine - firstLine + 1)
            .Select(i => text.Lines[i].ToString().TrimEnd())
            .ToList();

        var indent = raw
            .Where(line => line.Length > 0)
            .Select(line => line.Length - line.TrimStart().Length)
            .DefaultIfEmpty(0)
            .Min();
        var lines = raw.Select(line => line.Length == 0 ? line : line[indent..]).ToList();

        // Units: the signature through the opening brace, then each top-level statement. The closing
        // brace joins the last unit.
        var unitEnds = new List<int>();
        if (syntax.Body is { } block)
        {
            unitEnds.Add(LineOffset(block.OpenBraceToken.Span.End));
            foreach (var statement in block.Statements)
                unitEnds.Add(LineOffset(statement.Span.End));
        }

        return SemanticChunker.Chunk(lines, firstLine + 1, unitEnds, _tokenBudget);

        int LineOffset(int position) => text.Lines.GetLineFromPosition(position).LineNumber - firstLine;
    }

    private static TypeTrace DescribeType(INamedTypeSymbol type)
    {
        var baseTypes = new List<string>();
        for (var current = type.BaseType; current is not null; current = current.BaseType)
            baseTypes.Add(current.ToDisplayString(TypeFormat));

        var kind = type.TypeKind switch
        {
            TypeKind.Class => "class",
            TypeKind.Struct => "struct",
            TypeKind.Interface => "interface",
            _ => type.TypeKind.ToString().ToLowerInvariant(),
        };

        return new TypeTrace
        {
            FullyQualifiedName = type.ToDisplayString(TypeFormat),
            Kind = type.IsRecord ? $"record {kind}" : kind,
            BaseTypes = baseTypes,
            Interfaces = type.AllInterfaces
                .Select(i => i.ToDisplayString(TypeFormat))
                .Distinct()
                .Order(StringComparer.Ordinal)
                .ToList(),
            IsPartial = type.DeclaringSyntaxReferences.Length > 1,
        };
    }

    private static SignatureTrace DescribeSignature(IMethodSymbol method)
    {
        var modifiers = new List<string>();
        if (method.IsStatic) modifiers.Add("static");
        if (method.IsAbstract) modifiers.Add("abstract");
        if (method.IsVirtual) modifiers.Add("virtual");
        if (method.IsOverride) modifiers.Add("override");
        if (method.IsSealed) modifiers.Add("sealed");
        if (method.IsExtern) modifiers.Add("extern");
        if (method.IsAsync) modifiers.Add("async");

        return new SignatureTrace
        {
            Accessibility = method.DeclaredAccessibility switch
            {
                Accessibility.Public => "public",
                Accessibility.Private => "private",
                Accessibility.Protected => "protected",
                Accessibility.Internal => "internal",
                Accessibility.ProtectedOrInternal => "protected internal",
                Accessibility.ProtectedAndInternal => "private protected",
                _ => string.Empty,
            },
            Modifiers = modifiers,
            ReturnType = method.ReturnType.ToDisplayString(TypeFormat),
            Parameters = method.Parameters
                .Select(p => new ParameterTrace(p.Name, p.Type.ToDisplayString(TypeFormat), ParameterModifier(p), p.IsOptional))
                .ToList(),
            TypeParameters = method.TypeParameters.Select(t => t.Name).ToList(),
        };
    }

    private static string ParameterModifier(IParameterSymbol parameter)
    {
        if (parameter.IsParams)
            return "params";
        if (parameter.Ordinal == 0 && parameter.ContainingSymbol is IMethodSymbol { IsExtensionMethod: true })
            return "this";

        return parameter.RefKind switch
        {
            RefKind.Ref => "ref",
            RefKind.Out => "out",
            RefKind.In => "in",
            RefKind.RefReadOnlyParameter => "ref readonly",
            _ => string.Empty,
        };
    }

    private IReadOnlyList<CalleeTrace> CollectCallees(IEnumerable<IOperation> operations)
    {
        var callees = new Dictionary<string, (IMethodSymbol Symbol, int Count)>(StringComparer.Ordinal);
        foreach (var operation in operations)
        {
            IMethodSymbol? target = operation switch
            {
                IInvocationOperation invocation => invocation.TargetMethod,
                IObjectCreationOperation creation => creation.Constructor,
                IMethodReferenceOperation reference => reference.Method,
                _ => null,
            };

            // Lambdas and local functions are part of the traced body, not dependencies of it.
            if (target is null || target.MethodKind is MethodKind.AnonymousFunction or MethodKind.LocalFunction)
                continue;

            // Generic definitions keep the list stable: Where<Product> and Where<Order> are one API.
            var definition = (target.ReducedFrom ?? target).OriginalDefinition;
            var key = definition.ToDisplayString(MemberFormat);
            callees[key] = callees.TryGetValue(key, out var existing)
                ? (existing.Symbol, existing.Count + 1)
                : (definition, 1);
        }

        return callees
            .OrderBy(entry => entry.Key, StringComparer.Ordinal)
            .Select(entry =>
            {
                var (origin, container) = Locate(entry.Value.Symbol);
                return new CalleeTrace(entry.Key, origin, container, entry.Value.Count, IsObsolete(entry.Value.Symbol));
            })
            .ToList();
    }

    private async Task<IReadOnlyList<CallerTrace>> CollectCallersAsync(
        IMethodSymbol method,
        CancellationToken cancellationToken)
    {
        var infos = await SymbolFinder.FindCallersAsync(method, _solution, cancellationToken);

        var callers = new Dictionary<string, CallerAccumulator>(StringComparer.Ordinal);
        foreach (var info in infos)
        {
            var calling = EnclosingMember(info.CallingSymbol);
            var key = calling.ToDisplayString(MemberFormat);
            if (!callers.TryGetValue(key, out var accumulator))
            {
                accumulator = new CallerAccumulator(Locate(calling).Container);
                callers[key] = accumulator;
            }

            accumulator.AnyDirect |= info.IsDirect;

            // Multi-targeted projects report the same call site once per TFM; the set collapses them.
            foreach (var location in info.Locations.Where(l => l.IsInSource))
            {
                var span = location.GetLineSpan();
                accumulator.Sites.Add((
                    string.IsNullOrEmpty(span.Path) ? null : RelativeFile(span.Path),
                    span.StartLinePosition.Line + 1,
                    span.StartLinePosition.Character));
            }
        }

        return callers
            .OrderBy(entry => entry.Key, StringComparer.Ordinal)
            .Select(entry =>
            {
                var first = entry.Value.Sites
                    .OrderBy(site => site.File, StringComparer.Ordinal)
                    .ThenBy(site => site.Line)
                    .Select(site => ((string? File, int? Line)?)(site.File, site.Line))
                    .FirstOrDefault();

                return new CallerTrace(
                    entry.Key,
                    entry.Value.Project,
                    first?.File,
                    first?.Line,
                    entry.Value.Sites.Count,
                    ViaOverrideOrInterface: !entry.Value.AnyDirect);
            })
            .ToList();
    }

    /// <summary>A call made inside a lambda or local function belongs to the member that declares it.</summary>
    private static ISymbol EnclosingMember(ISymbol symbol)
    {
        while (symbol is IMethodSymbol { MethodKind: MethodKind.AnonymousFunction or MethodKind.LocalFunction }
               && symbol.ContainingSymbol is { } parent)
        {
            symbol = parent;
        }

        return symbol;
    }

    /// <summary>
    /// Fields, properties and events of solution types. Framework members such as
    /// <c>string.Length</c> are left out: they say nothing about the domain state a rule depends on.
    /// </summary>
    private IReadOnlyList<MemberAccessTrace> CollectState(IEnumerable<IOperation> operations)
    {
        var members = new Dictionary<string, (string Kind, MemberAccessKind Access)>(StringComparer.Ordinal);
        foreach (var operation in operations)
        {
            ISymbol member;
            string kind;
            switch (operation)
            {
                case IFieldReferenceOperation field:
                    member = field.Field;
                    kind = "Field";
                    break;
                case IPropertyReferenceOperation property:
                    member = property.Property;
                    kind = "Property";
                    break;
                case IEventReferenceOperation @event:
                    member = @event.Event;
                    kind = "Event";
                    break;
                default:
                    continue;
            }

            // Query syntax and anonymous objects materialize compiler-generated members
            // (<>h__TransparentIdentifier0, new { p, pc }.pc). They are query plumbing, not state, and
            // listing them as writes would misreport a read-only query as mutating the domain.
            if (!member.CanBeReferencedByName || member.ContainingType is { IsAnonymousType: true })
                continue;

            if (Locate(member).Origin != SymbolOrigin.Source)
                continue;

            var key = member.OriginalDefinition.ToDisplayString(MemberFormat);
            var access = ClassifyAccess(operation);
            members[key] = members.TryGetValue(key, out var existing)
                ? (existing.Kind, existing.Access == access ? access : MemberAccessKind.ReadWrite)
                : (kind, access);
        }

        return members
            .OrderBy(entry => entry.Key, StringComparer.Ordinal)
            .Select(entry => new MemberAccessTrace(entry.Key, entry.Value.Kind, entry.Value.Access))
            .ToList();
    }

    private static MemberAccessKind ClassifyAccess(IOperation reference) => reference.Parent switch
    {
        ISimpleAssignmentOperation assignment when ReferenceEquals(assignment.Target, reference) => MemberAccessKind.Write,
        ICompoundAssignmentOperation compound when ReferenceEquals(compound.Target, reference) => MemberAccessKind.ReadWrite,
        ICoalesceAssignmentOperation coalesce when ReferenceEquals(coalesce.Target, reference) => MemberAccessKind.ReadWrite,
        IIncrementOrDecrementOperation increment when ReferenceEquals(increment.Target, reference) => MemberAccessKind.ReadWrite,
        IArgumentOperation { Parameter.RefKind: RefKind.Out } => MemberAccessKind.Write,
        IArgumentOperation { Parameter.RefKind: RefKind.Ref } => MemberAccessKind.ReadWrite,
        _ => MemberAccessKind.Read,
    };

    private static DataFlowTrace? AnalyzeDataFlow(SemanticModel model, BaseMethodDeclarationSyntax syntax, IMethodSymbol method)
    {
        DataFlowAnalysis? flow = syntax switch
        {
            { Body: { } block } => model.AnalyzeDataFlow(block),
            { ExpressionBody: { } arrow } => model.AnalyzeDataFlow(arrow.Expression),
            _ => null,
        };

        if (flow is not { Succeeded: true })
            return null;

        return new DataFlowTrace(
            ParameterNames(flow.ReadInside),
            ParameterNames(flow.WrittenInside),
            flow.Captured
                .Where(s => s is not IParameterSymbol { IsThis: true })
                .Select(s => s.Name)
                .Distinct()
                .Order(StringComparer.Ordinal)
                .ToList(),
            flow.VariablesDeclared.Count(s => s is ILocalSymbol));

        // Lambda parameters also appear in the analysis; only the method's own parameters matter here.
        IReadOnlyList<string> ParameterNames(IEnumerable<ISymbol> symbols) =>
            symbols
                .OfType<IParameterSymbol>()
                .Where(p => !p.IsThis && SymbolEqualityComparer.Default.Equals(p.ContainingSymbol, method))
                .Select(p => p.Name)
                .Distinct()
                .Order(StringComparer.Ordinal)
                .ToList();
    }

    private static ControlFlowTrace? SummarizeControlFlow(IOperation? body, CancellationToken cancellationToken)
    {
        var graph = body switch
        {
            IMethodBodyOperation method => ControlFlowGraph.Create(method, cancellationToken),
            IConstructorBodyOperation constructor => ControlFlowGraph.Create(constructor, cancellationToken),
            _ => null,
        };

        if (graph is null)
            return null;

        int conditional = 0, backEdges = 0, exits = 0, throws = 0;
        foreach (var block in graph.Blocks)
        {
            if (block.ConditionalSuccessor is { } conditionalBranch)
            {
                conditional++;
                Inspect(block, conditionalBranch);
            }

            if (block.FallThroughSuccessor is { } fallThrough)
            {
                Inspect(block, fallThrough);
                if (fallThrough.Semantics is ControlFlowBranchSemantics.Throw or ControlFlowBranchSemantics.Rethrow)
                    throws++;
            }
        }

        return new ControlFlowTrace(
            graph.Blocks.Count(b => b.Kind == BasicBlockKind.Block),
            conditional,
            backEdges,
            CountHandlers(graph.Root),
            exits,
            throws);

        // Blocks are numbered in source order, so a branch to an earlier block closes a loop.
        void Inspect(BasicBlock source, ControlFlowBranch branch)
        {
            if (branch.Destination is not { } destination)
                return;

            if (destination.Kind == BasicBlockKind.Exit)
                exits++;
            else if (destination.Ordinal <= source.Ordinal)
                backEdges++;
        }
    }

    private static int CountHandlers(ControlFlowRegion region) =>
        (region.Kind is ControlFlowRegionKind.Catch or ControlFlowRegionKind.Finally ? 1 : 0)
        + region.NestedRegions.Sum(CountHandlers);

    private static GroundingTrace AssessGrounding(
        SemanticModel model,
        SyntaxNode syntax,
        IReadOnlyCollection<IOperation> operations,
        CancellationToken cancellationToken)
    {
        var errors = model.GetDiagnostics(syntax.Span, cancellationToken)
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();

        return new GroundingTrace(
            errors.Count,
            operations.Count(o => o is IInvalidOperation),
            errors
                .Select(d => $"{d.Id}: {d.GetMessage(CultureInfo.InvariantCulture)}")
                .Distinct()
                .Order(StringComparer.Ordinal)
                .Take(MaxReportedErrors)
                .ToList());
    }

    private IReadOnlyList<TraceIncident> IncidentsWithin(string relativeFile, int startLine, int endLine) =>
        _incidentsByFile[relativeFile]
            .Where(i => i.Line >= startLine && i.Line <= endLine)
            .OrderBy(i => i.Line)
            .ThenBy(i => i.RuleId, StringComparer.Ordinal)
            .Select(i => new TraceIncident(i.RuleId, i.Severity, i.Label, i.Line))
            .ToList();

    private (SymbolOrigin Origin, string Container) Locate(ISymbol symbol)
    {
        var assembly = symbol.ContainingAssembly?.Identity.Name;
        if (assembly is not null && _projectsByAssembly.TryGetValue(assembly, out var project))
            return (SymbolOrigin.Source, project);

        var tree = symbol.Locations.FirstOrDefault(l => l.IsInSource)?.SourceTree;
        if (tree is not null && _solution.GetDocument(tree) is { } document)
            return (SymbolOrigin.Source, ProjectGraphLoader.NormalizeProjectName(document.Project.Name));

        return (SymbolOrigin.Metadata, assembly ?? "(unknown)");
    }

    private string RelativeFile(string path)
    {
        // In-memory documents carry a logical name, not a location; resolving it would anchor it to
        // whatever the current working directory happens to be.
        if (!Path.IsPathRooted(path))
            return NormalizePath(path);

        var relative = Path.GetRelativePath(_solutionDirectory, path);

        // Files outside the solution directory — linked files, another drive — have no stable relative
        // form, and "../../.." would expose the machine's directory layout in output meant to be
        // shared. Keep only the file name.
        var outside = Path.IsPathRooted(relative)
            || relative == ".."
            || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            || relative.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal);

        return outside ? Path.GetFileName(path) : NormalizePath(relative);
    }

    internal static string NormalizePath(string path) => path.Replace('\\', '/').TrimStart('/');

    private static bool IsObsolete(ISymbol symbol) =>
        symbol.GetAttributes().Any(a => a.AttributeClass?.Name == nameof(ObsoleteAttribute));

    private sealed class CallerAccumulator(string project)
    {
        public string Project { get; } = project;

        public bool AnyDirect { get; set; }

        public HashSet<(string? File, int Line, int Column)> Sites { get; } = [];
    }
}
