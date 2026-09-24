using LegacyLens.Core.Tracing;
using LegacyLens.Core.Tracing.Models;

namespace LegacyLens.Core.Tests;

public sealed class TraceOutputWriterTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"legacylens-traces-{Guid.NewGuid():N}");

    [Theory]
    [InlineData("Nop.Services.Catalog.ProductService.SearchProducts(out System.Collections.Generic.IList<int>, bool)", "ProductService.SearchProducts")]
    [InlineData("Alpha.Repository<T>.Find<TKey>(TKey)", "Repository.Find")]
    [InlineData("Alpha.OrderRules.OrderRules()", "OrderRules.OrderRules")]
    public void Slug_UsesContainingTypeAndMemberName(string fullyQualifiedName, string expected) =>
        Assert.Equal(expected, TraceOutputWriter.Slug(fullyQualifiedName));

    [Fact]
    public async Task WriteTraces_IsDeterministicAndOnlyRemovesItsOwnStaleFiles()
    {
        var trace = SampleTrace();
        var first = await TraceOutputWriter.WriteTracesAsync(_directory, [trace]);
        var firstJson = await File.ReadAllTextAsync(first[0].JsonPath);

        var tracesDirectory = Path.Combine(_directory, TraceOutputWriter.TracesFolder);
        var stale = Path.Combine(tracesDirectory, "002-Old.Method-0123abcd.md");
        var unrelated = Path.Combine(tracesDirectory, "notes.txt");
        await File.WriteAllTextAsync(stale, "stale");
        await File.WriteAllTextAsync(unrelated, "keep");

        var second = await TraceOutputWriter.WriteTracesAsync(_directory, [trace]);

        Assert.Equal(first[0].JsonPath, second[0].JsonPath);
        Assert.Equal(firstJson, await File.ReadAllTextAsync(second[0].JsonPath));
        Assert.DoesNotContain("\r", firstJson);
        Assert.False(File.Exists(stale));
        Assert.True(File.Exists(unrelated));

        var markdown = await File.ReadAllTextAsync(second[0].MarkdownPath);
        Assert.Contains("fully resolved by the compiler", markdown);
        Assert.Contains("```csharp", markdown);
    }

    private static MethodTrace SampleTrace() => new()
    {
        Id = "M:Alpha.OrderRules.Apply(System.Decimal)",
        FullyQualifiedName = "Alpha.OrderRules.Apply(decimal)",
        Project = "Alpha",
        File = "Alpha/OrderRules.cs",
        StartLine = 10,
        EndLine = 12,
        Complexity = 1,
        IsObsolete = false,
        ContainingType = new TypeTrace
        {
            FullyQualifiedName = "Alpha.OrderRules",
            Kind = "class",
            BaseTypes = ["object"],
            Interfaces = [],
            IsPartial = false,
        },
        Signature = new SignatureTrace
        {
            Accessibility = "public",
            Modifiers = [],
            ReturnType = "decimal",
            Parameters = [new ParameterTrace("amount", "decimal", string.Empty, false)],
            TypeParameters = [],
        },
        Callees = [],
        Callers = [],
        State = [],
        DataFlow = null,
        ControlFlow = null,
        Grounding = new GroundingTrace(0, 0, []),
        AppCatIncidents = [],
        Chunks = [new TraceChunk(0, 10, 12, 10, ChunkBoundary.Whole, "public decimal Apply(decimal amount)\n{\n}")],
    };

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }
}
