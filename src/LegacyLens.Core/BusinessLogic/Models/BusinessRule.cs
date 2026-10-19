namespace LegacyLens.Core.BusinessLogic.Models;

/// <summary>
/// The categories of domain knowledge the extraction targets (TDD §6.3). Legacy code encodes these
/// as control flow and naming rather than as documentation, which is why they are lost in
/// conventional migrations.
/// </summary>
public enum BusinessRuleKind
{
    /// <summary>A condition the code holds true throughout an operation.</summary>
    Invariant,

    /// <summary>A condition required before the operation may proceed.</summary>
    Precondition,

    /// <summary>A condition guaranteed once the operation completes.</summary>
    Postcondition,

    /// <summary>An expectation about an external system, message shape, or protocol.</summary>
    IntegrationContract,

    /// <summary>A constraint imposed by regulation rather than by the domain itself.</summary>
    RegulatoryConstraint,

    /// <summary>Business meaning carried by how the code raises or handles failure.</summary>
    ExceptionSemantics,

    Other,
}

/// <summary>
/// The four-dimension evaluation rubric from the MITRE study on LLM-assisted documentation of legacy
/// code (Diggs et al., arXiv:2411.14971), adopted in TDD §6.3.
/// </summary>
/// <remarks>
/// Every dimension is scored on the same 1–4 scale and in the same direction: higher is better. For
/// hallucination that means 4 denotes an extraction with no unsupported content, not the reverse.
/// Keeping one direction for all four is what makes a single threshold meaningful.
/// </remarks>
public sealed record RubricScore
{
    public const int MinimumScore = 1;
    public const int MaximumScore = 4;

    /// <summary>TDD §6.3 persists only extractions scoring at or above this value.</summary>
    public const int DefaultPersistenceThreshold = 3;

    public RubricScore(int hallucination, int completeness, int readability, int usefulness)
    {
        Hallucination = Validate(hallucination, nameof(hallucination));
        Completeness = Validate(completeness, nameof(completeness));
        Readability = Validate(readability, nameof(readability));
        Usefulness = Validate(usefulness, nameof(usefulness));
    }

    /// <summary>Freedom from content the source does not support; 4 means nothing unsupported.</summary>
    public int Hallucination { get; }

    /// <summary>How much of the rule present in the source was captured.</summary>
    public int Completeness { get; }

    /// <summary>Whether the statement is intelligible to an engineer unfamiliar with the code.</summary>
    public int Readability { get; }

    /// <summary>Whether the statement would help someone modernizing the code. The TDD milestone tracks this dimension.</summary>
    public int Usefulness { get; }

    public IReadOnlyList<int> Dimensions => [Hallucination, Completeness, Readability, Usefulness];

    /// <summary>
    /// Whether every dimension reaches the threshold. Requiring all four, rather than an average,
    /// prevents a fluent but unsupported statement from passing on readability alone.
    /// </summary>
    public bool MeetsThreshold(int minimum = DefaultPersistenceThreshold) =>
        Dimensions.All(dimension => dimension >= minimum);

    private static int Validate(int value, string name) =>
        value is >= MinimumScore and <= MaximumScore
            ? value
            : throw new ArgumentOutOfRangeException(
                name, value, $"Rubric dimensions are scored from {MinimumScore} to {MaximumScore}.");
}

/// <summary>
/// One piece of domain knowledge recovered from a method, together with the provenance needed to
/// judge it later: which model produced it, when, and how it scored against the rubric.
/// </summary>
public sealed record BusinessRule
{
    /// <summary>Roslyn documentation comment id of the method, which joins this rule to its trace.</summary>
    public required string SymbolId { get; init; }

    public required string FullyQualifiedName { get; init; }

    public required string Project { get; init; }

    /// <summary>Path relative to the solution directory, with forward slashes.</summary>
    public string? File { get; init; }

    public required BusinessRuleKind Kind { get; init; }

    /// <summary>The rule itself, stated in prose.</summary>
    public required string Statement { get; init; }

    /// <summary>Null until the extraction has been evaluated against the rubric.</summary>
    public RubricScore? Score { get; init; }

    /// <summary>
    /// The exact model version that produced the statement. Pinning it is what allows a later run to
    /// be compared against this one rather than merely repeated.
    /// </summary>
    public required string ModelSnapshot { get; init; }

    public required DateTime ExtractedUtc { get; init; }

    /// <summary>Whether this rule qualifies for persistence under the TDD's confidence threshold.</summary>
    public bool IsHighConfidence(int minimum = RubricScore.DefaultPersistenceThreshold) =>
        Score is not null && Score.MeetsThreshold(minimum);
}
