using LegacyLens.Core.Tracing;
using LegacyLens.Core.Tracing.Models;

namespace LegacyLens.Core.Tests;

public sealed class SemanticChunkerTests
{
    [Fact]
    public void Chunk_WithinBudget_KeepsMemberWhole()
    {
        string[] lines = ["void M()", "{", "    Run();", "}"];

        var chunk = Assert.Single(SemanticChunker.Chunk(lines, firstLineNumber: 10, unitEnds: [1, 2], tokenBudget: 1000));

        Assert.Equal(ChunkBoundary.Whole, chunk.Boundary);
        Assert.Equal(10, chunk.StartLine);
        Assert.Equal(13, chunk.EndLine);
        Assert.Equal(string.Join('\n', lines), chunk.Text);
    }

    [Fact]
    public void Chunk_OverBudget_SplitsBetweenStatementsWithoutGapsOrOverlap()
    {
        var lines = new List<string> { "public void Process()", "{" };
        for (var i = 0; i < 12; i++)
            lines.Add($"    Step{i:D2}(order, customer, inventory);");
        lines.Add("}");

        // The opening brace and each statement close a unit; the closing brace joins the last one.
        var chunks = SemanticChunker.Chunk(lines, 1, Enumerable.Range(1, 13).ToList(), tokenBudget: 40);

        Assert.True(chunks.Count > 1);
        Assert.Equal(Enumerable.Range(0, chunks.Count), chunks.Select(c => c.Index));
        Assert.All(chunks, c => Assert.Equal(ChunkBoundary.Statement, c.Boundary));
        Assert.All(chunks, c => Assert.True(c.EstimatedTokens <= 40, $"chunk {c.Index} has {c.EstimatedTokens} tokens"));
        AssertContiguousCoverage(lines, chunks);
    }

    [Fact]
    public void Chunk_StatementLargerThanBudget_FallsBackToLineBoundaries()
    {
        var lines = new List<string> { "void M()", "{", "    var values = new[]", "    {" };
        for (var i = 0; i < 20; i++)
            lines.Add($"        \"value number {i:D2} in a long initializer\",");
        lines.Add("    };");
        lines.Add("}");

        var chunks = SemanticChunker.Chunk(lines, 1, [1, lines.Count - 2], tokenBudget: 50);

        Assert.Contains(chunks, c => c.Boundary == ChunkBoundary.Line);
        Assert.All(chunks, c => Assert.True(c.EstimatedTokens <= 50, $"chunk {c.Index} has {c.EstimatedTokens} tokens"));
        AssertContiguousCoverage(lines, chunks);
    }

    [Fact]
    public void Chunk_RejectsNonPositiveBudget() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => SemanticChunker.Chunk(["x"], 1, [], tokenBudget: 0));

    private static void AssertContiguousCoverage(IReadOnlyList<string> lines, IReadOnlyList<TraceChunk> chunks)
    {
        Assert.Equal(1, chunks[0].StartLine);
        Assert.Equal(lines.Count, chunks[^1].EndLine);
        for (var i = 1; i < chunks.Count; i++)
            Assert.Equal(chunks[i - 1].EndLine + 1, chunks[i].StartLine);

        Assert.Equal(string.Join('\n', lines), string.Join('\n', chunks.Select(c => c.Text)));
    }
}
