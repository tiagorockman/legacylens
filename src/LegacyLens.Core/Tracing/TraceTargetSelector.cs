using LegacyLens.Core.Analysis;
using LegacyLens.Core.Ingestion;
using Microsoft.CodeAnalysis;

namespace LegacyLens.Core.Tracing;

public sealed record TraceSelection
{
    /// <summary>Case-insensitive substring of the method's fully-qualified name.</summary>
    public string? MethodFilter { get; init; }

    /// <summary>Restrict tracing to one project, by name.</summary>
    public string? ProjectFilter { get; init; }

    /// <summary>Maximum number of methods to trace, taken in descending complexity order.</summary>
    public int Top { get; init; } = 20;

    public int MinComplexity { get; init; } = 1;

    /// <summary>Test code documents behavior but rarely holds the business rules being recovered.</summary>
    public bool IncludeTestProjects { get; init; }
}

public sealed record TraceTarget(Project Project, IMethodSymbol Method, string FullyQualifiedName, int Complexity);

/// <summary>
/// Chooses which methods to trace. Complexity drives the ranking because in legacy code the most
/// branched methods are where undocumented business rules accumulate.
/// </summary>
public static class TraceTargetSelector
{
    public static async Task<IReadOnlyList<TraceTarget>> SelectAsync(
        Solution solution,
        ProjectGraph graph,
        TraceSelection selection,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(selection.Top);

        var testProjectFiles = graph.Projects
            .Where(p => p.IsTestProject)
            .Select(p => p.FilePath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var candidates = new List<TraceTarget>();
        var seenProjects = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var project in solution.Projects.OrderBy(p => p.Name, StringComparer.Ordinal))
        {
            if (project.Language != LanguageNames.CSharp)
                continue;

            // Multi-targeted projects compile once per TFM; trace the first flavor only.
            if (project.FilePath is not null && !seenProjects.Add(project.FilePath))
                continue;

            if (!selection.IncludeTestProjects && project.FilePath is not null && testProjectFiles.Contains(project.FilePath))
                continue;

            if (selection.ProjectFilter is not null
                && !string.Equals(
                    ProjectGraphLoader.NormalizeProjectName(project.Name),
                    selection.ProjectFilter,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var compilation = await project.GetCompilationAsync(cancellationToken);
            if (compilation is null)
                continue;

            foreach (var type in SymbolExtractor.EnumerateSourceTypes(compilation.Assembly.GlobalNamespace))
            {
                if (IsGeneratedCode(type))
                    continue;

                foreach (var method in type.GetMembers().OfType<IMethodSymbol>())
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (method.MethodKind is not (MethodKind.Ordinary or MethodKind.Constructor)
                        || method.IsImplicitlyDeclared
                        || IsGeneratedCode(method))
                    {
                        continue;
                    }

                    var syntax = TraceBuilder.FindBodySyntax(method, cancellationToken);
                    if (syntax is null || IsGeneratedFile(syntax.SyntaxTree.FilePath))
                        continue;

                    var name = method.ToDisplayString(TraceBuilder.MemberFormat);
                    if (selection.MethodFilter is not null
                        && !name.Contains(selection.MethodFilter, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var complexity = ComplexityScorer.Score(syntax);
                    if (complexity >= selection.MinComplexity)
                        candidates.Add(new TraceTarget(project, method, name, complexity));
                }
            }
        }

        return candidates
            .OrderByDescending(c => c.Complexity)
            .ThenBy(c => c.FullyQualifiedName, StringComparer.Ordinal)
            .Take(selection.Top)
            .ToList();
    }

    /// <summary>WCF service references, designers and T4 output carry no hand-written business rules.</summary>
    private static bool IsGeneratedCode(ISymbol symbol) =>
        symbol.GetAttributes().Any(a => a.AttributeClass?.Name is "GeneratedCodeAttribute" or "CompilerGeneratedAttribute");

    internal static bool IsGeneratedFile(string? path)
    {
        if (string.IsNullOrEmpty(path))
            return false;

        var name = Path.GetFileName(path);
        if (name.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".g.i.cs", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".designer.cs", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".generated.cs", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return path
            .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(segment => segment.Equals("obj", StringComparison.OrdinalIgnoreCase));
    }
}
