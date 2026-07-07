using LegacyLens.Core.Analysis;
using LegacyLens.Core.Analysis.Models;
using LegacyLens.Core.Ingestion;

namespace LegacyLens.Core.Tests;

[Collection(SampleSolutionCollection.Name)]
public sealed class SymbolExtractorTests(SampleSolutionFixture fixture)
{
    [Fact]
    public async Task ExtractAsync_IndexesTypesMethodsComplexityAndDeprecation()
    {
        var loader = new ProjectGraphLoader();
        using var analysis = await loader.LoadAsync(fixture.SolutionPath);
        var alphaProject = analysis.Solution.Projects.Single(p => p.Name == "Alpha");

        var extractor = new SymbolExtractor();
        var result = await extractor.ExtractAsync(alphaProject);

        Assert.Equal(0, result.CompilationErrorCount);

        var service = Assert.Single(result.Symbols,
            s => s is { Kind: SymbolKind.Type, FullyQualifiedName: "Alpha.AlphaService" });
        Assert.False(service.IsDeprecated);
        Assert.NotNull(service.FilePath);

        // 1 (base) + foreach + if = 3
        var sumPositives = Assert.Single(result.Symbols,
            s => s.Kind == SymbolKind.Method && s.FullyQualifiedName.Contains("SumPositives"));
        Assert.Equal(3, sumPositives.Complexity);

        var legacySum = Assert.Single(result.Symbols,
            s => s.Kind == SymbolKind.Method && s.FullyQualifiedName.Contains("LegacySum"));
        Assert.True(legacySum.IsDeprecated);
    }
}
