using LegacyLens.Core.Analysis.Models;
using Microsoft.CodeAnalysis;

namespace LegacyLens.Core.Analysis;

/// <summary>
/// TDD Layer 2 — Static Analysis (initial slice). Walks each project's compilation and
/// indexes source-declared types and methods with fully-qualified names, cyclomatic
/// complexity, and deprecation flags.
/// </summary>
public sealed class SymbolExtractor
{
    private static readonly SymbolDisplayFormat DisplayFormat = SymbolDisplayFormat.CSharpErrorMessageFormat;

    public async Task<ProjectSymbols> ExtractAsync(Project project, CancellationToken cancellationToken = default)
    {
        var compilation = await project.GetCompilationAsync(cancellationToken)
            ?? throw new InvalidOperationException($"Project '{project.Name}' does not support compilation.");

        var errors = compilation.GetDiagnostics(cancellationToken)
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();

        var topErrors = errors
            .GroupBy(d => d.Id)
            .OrderByDescending(g => g.Count())
            .Take(5)
            .Select(g => new DiagnosticSummary(g.Key, g.Count(), g.First().GetMessage()))
            .ToList();

        var records = new List<SymbolRecord>();
        foreach (var type in EnumerateSourceTypes(compilation.Assembly.GlobalNamespace))
        {
            cancellationToken.ThrowIfCancellationRequested();

            records.Add(new SymbolRecord
            {
                ProjectName = project.Name,
                FullyQualifiedName = type.ToDisplayString(DisplayFormat),
                Kind = Models.SymbolKind.Type,
                FilePath = SourceFilePath(type),
                Complexity = 0,
                IsDeprecated = IsObsolete(type),
            });

            foreach (var method in type.GetMembers().OfType<IMethodSymbol>())
            {
                if (method.MethodKind is not (MethodKind.Ordinary or MethodKind.Constructor))
                    continue;
                if (method.IsImplicitlyDeclared)
                    continue;

                records.Add(new SymbolRecord
                {
                    ProjectName = project.Name,
                    FullyQualifiedName = method.ToDisplayString(DisplayFormat),
                    Kind = Models.SymbolKind.Method,
                    FilePath = SourceFilePath(method),
                    Complexity = ComputeComplexity(method, cancellationToken),
                    IsDeprecated = IsObsolete(method),
                });
            }
        }

        return new ProjectSymbols(project.Name, errors.Count, records) { TopErrors = topErrors };
    }

    private static IEnumerable<INamedTypeSymbol> EnumerateSourceTypes(INamespaceSymbol root)
    {
        var namespaces = new Stack<INamespaceSymbol>();
        namespaces.Push(root);

        while (namespaces.Count > 0)
        {
            var current = namespaces.Pop();
            foreach (var nested in current.GetNamespaceMembers())
                namespaces.Push(nested);

            foreach (var type in current.GetTypeMembers())
            {
                foreach (var declared in FlattenNestedTypes(type))
                {
                    if (declared.Locations.Any(l => l.IsInSource))
                        yield return declared;
                }
            }
        }
    }

    private static IEnumerable<INamedTypeSymbol> FlattenNestedTypes(INamedTypeSymbol type)
    {
        yield return type;
        foreach (var nested in type.GetTypeMembers())
        {
            foreach (var inner in FlattenNestedTypes(nested))
                yield return inner;
        }
    }

    private static int ComputeComplexity(IMethodSymbol method, CancellationToken cancellationToken)
    {
        var syntaxReference = method.DeclaringSyntaxReferences.FirstOrDefault();
        if (syntaxReference is null)
            return 0;

        return ComplexityScorer.Score(syntaxReference.GetSyntax(cancellationToken));
    }

    private static bool IsObsolete(ISymbol symbol) =>
        symbol.GetAttributes().Any(a => a.AttributeClass?.Name == nameof(ObsoleteAttribute));

    private static string? SourceFilePath(ISymbol symbol) =>
        symbol.Locations.FirstOrDefault(l => l.IsInSource)?.SourceTree?.FilePath;
}
