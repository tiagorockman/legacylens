using LegacyLens.Core.Tracing.Models;

namespace LegacyLens.Core.Tracing;

/// <summary>
/// Splits a member's source into LLM-sized chunks along semantic boundaries — between top-level
/// statements, never mid-statement unless a single statement alone exceeds the budget.
/// </summary>
/// <remarks>
/// Token counts are estimated at four characters per token rather than taken from a model
/// tokenizer. The estimate only has to keep chunks comfortably inside a context window, and keeping
/// it tokenizer-free makes chunking identical across runs, machines and LLM providers — a
/// precondition for the reproducible runs the TDD requires.
/// </remarks>
public static class SemanticChunker
{
    public const int CharactersPerToken = 4;

    public static int EstimateTokens(string text) => EstimateTokens(text.Length);

    private static int EstimateTokens(int characters) => (characters + CharactersPerToken - 1) / CharactersPerToken;

    /// <param name="lines">The member's source lines, already dedented.</param>
    /// <param name="firstLineNumber">1-based source line number of <paramref name="lines"/>[0].</param>
    /// <param name="unitEnds">
    /// 0-based indexes into <paramref name="lines"/> of the last line of each semantic unit. Units are
    /// contiguous, so comments between statements travel with the statement that follows them.
    /// </param>
    /// <param name="tokenBudget">Maximum estimated tokens per chunk.</param>
    public static IReadOnlyList<TraceChunk> Chunk(
        IReadOnlyList<string> lines,
        int firstLineNumber,
        IReadOnlyList<int> unitEnds,
        int tokenBudget)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tokenBudget);
        if (lines.Count == 0)
            return [];

        var prefix = new int[lines.Count + 1];
        for (var i = 0; i < lines.Count; i++)
            prefix[i + 1] = prefix[i] + lines[i].Length;

        var lastLine = lines.Count - 1;
        var chunks = new List<TraceChunk>();

        if (Tokens(0, lastLine) <= tokenBudget)
        {
            Add(0, lastLine, ChunkBoundary.Whole);
            return chunks;
        }

        var ends = unitEnds
            .Where(e => e >= 0 && e < lastLine)
            .Distinct()
            .Order()
            .Append(lastLine)
            .ToList();

        int? groupStart = null;
        var groupEnd = -1;
        var unitStart = 0;
        foreach (var unitEnd in ends)
        {
            if (Tokens(unitStart, unitEnd) > tokenBudget)
            {
                FlushGroup();
                SplitByLines(unitStart, unitEnd);
            }
            else if (groupStart is int start && Tokens(start, unitEnd) > tokenBudget)
            {
                FlushGroup();
                groupStart = unitStart;
                groupEnd = unitEnd;
            }
            else
            {
                groupStart ??= unitStart;
                groupEnd = unitEnd;
            }

            unitStart = unitEnd + 1;
        }

        FlushGroup();
        return chunks;

        // Joined with '\n', so a range spanning n lines carries n - 1 separators.
        int Tokens(int first, int last) =>
            EstimateTokens(prefix[last + 1] - prefix[first] + (last - first));

        void Add(int first, int last, ChunkBoundary boundary) =>
            chunks.Add(new TraceChunk(
                chunks.Count,
                firstLineNumber + first,
                firstLineNumber + last,
                Tokens(first, last),
                boundary,
                string.Join('\n', Enumerable.Range(first, last - first + 1).Select(i => lines[i]))));

        void FlushGroup()
        {
            if (groupStart is not int start)
                return;

            Add(start, groupEnd, ChunkBoundary.Statement);
            groupStart = null;
        }

        void SplitByLines(int first, int last)
        {
            var start = first;
            for (var line = first + 1; line <= last; line++)
            {
                if (Tokens(start, line) <= tokenBudget)
                    continue;

                // A single line longer than the whole budget still becomes its own chunk; it cannot
                // be split further without cutting a token in half.
                Add(start, line - 1, ChunkBoundary.Line);
                start = line;
            }

            Add(start, last, ChunkBoundary.Line);
        }
    }
}
