namespace LegacyLens.Core.Constraints.Models;

/// <summary>
/// Outcome of evaluating one organizational rule against the analyzed solution. The four values are
/// deliberate: a rule the framework cannot decide statically must say so rather than silently pass.
/// </summary>
public enum ConstraintVerdict
{
    Compliant,
    NonCompliant,
    RequiresHumanReview,
    NotApplicable,
}

public enum ConstraintRuleKind
{
    /// <summary>Named packages must not be referenced.</summary>
    ProhibitedPackages,

    /// <summary>Only the named packages may be referenced; anything else is a violation.</summary>
    ApprovedPackages,

    /// <summary>Named packages must be referenced at or above a minimum version.</summary>
    MinimumPackageVersions,

    /// <summary>Every project must target one of the named frameworks.</summary>
    ApprovedTargetFrameworks,

    /// <summary>A requirement the organization expects a person to verify.</summary>
    ManualReview,

    /// <summary>A rule kind this version of the framework does not know how to evaluate.</summary>
    Unsupported,
}

public sealed record ConstraintRule
{
    public required string Id { get; init; }

    public required string Title { get; init; }

    public required ConstraintRuleKind Kind { get; init; }

    /// <summary>The kind exactly as written in the policy, retained for unsupported kinds.</summary>
    public string? DeclaredKind { get; init; }

    public string? Rationale { get; init; }

    /// <summary>Project name patterns ('*' wildcard) this rule applies to; empty means every project.</summary>
    public IReadOnlyList<string> AppliesTo { get; init; } = [];

    public IReadOnlyList<string> Packages { get; init; } = [];

    /// <summary>Package name to minimum acceptable version.</summary>
    public IReadOnlyDictionary<string, string> PackageVersions { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<string> Frameworks { get; init; } = [];

    /// <summary>The requirement text for a rule a person must verify.</summary>
    public string? Requirement { get; init; }
}

public sealed record ConstraintPolicy
{
    public const int CurrentSchemaVersion = 1;

    public required int SchemaVersion { get; init; }

    public string? Organization { get; init; }

    public string? Description { get; init; }

    public required IReadOnlyList<ConstraintRule> Rules { get; init; }

    /// <summary>File name of the policy, for the record of what was evaluated.</summary>
    public required string SourceName { get; init; }
}

/// <param name="Project">Project the finding was observed in.</param>
/// <param name="Detail">What was observed, in terms an engineer can act on.</param>
public sealed record ConstraintFinding(string Project, string Detail);

public sealed record ConstraintRuleResult
{
    public required string RuleId { get; init; }

    public required string Title { get; init; }

    public required ConstraintRuleKind Kind { get; init; }

    public required ConstraintVerdict Verdict { get; init; }

    public required int ProjectsEvaluated { get; init; }

    /// <summary>Observations that breach the rule.</summary>
    public required IReadOnlyList<ConstraintFinding> Violations { get; init; }

    /// <summary>Observations the framework could not decide, which a person must settle.</summary>
    public required IReadOnlyList<ConstraintFinding> Unresolved { get; init; }

    public string? Rationale { get; init; }

    /// <summary>Why this verdict was reached, when the reason is not the findings themselves.</summary>
    public string? Reason { get; init; }
}

public sealed record ConstraintEvaluation
{
    public required ConstraintPolicy Policy { get; init; }

    public required IReadOnlyList<ConstraintRuleResult> Results { get; init; }

    public int Count(ConstraintVerdict verdict) => Results.Count(r => r.Verdict == verdict);
}
