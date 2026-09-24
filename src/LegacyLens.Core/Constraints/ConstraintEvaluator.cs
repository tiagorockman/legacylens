using System.Text.RegularExpressions;
using LegacyLens.Core.Constraints.Models;
using LegacyLens.Core.Ingestion.Models;

namespace LegacyLens.Core.Constraints;

/// <summary>
/// Evaluates an organization's technical constraints against the analyzed solution, deterministically
/// and without any model inference.
/// </summary>
/// <remarks>
/// The evaluator never guesses. Where a rule cannot be decided from the evidence — an unparseable
/// package version, an unresolved target framework, a requirement that is inherently a matter of
/// judgment, or a rule kind this version does not implement — the result is
/// <see cref="ConstraintVerdict.RequiresHumanReview"/> rather than a pass.
/// </remarks>
public static partial class ConstraintEvaluator
{
    public static ConstraintEvaluation Evaluate(ConstraintPolicy policy, IReadOnlyList<ProjectInfo> projects)
    {
        var results = policy.Rules.Select(rule => EvaluateRule(rule, projects)).ToList();
        return new ConstraintEvaluation { Policy = policy, Results = results };
    }

    private static ConstraintRuleResult EvaluateRule(ConstraintRule rule, IReadOnlyList<ProjectInfo> projects)
    {
        var applicable = projects.Where(p => AppliesTo(rule, p.Name)).ToList();
        if (applicable.Count == 0)
        {
            return Result(rule, ConstraintVerdict.NotApplicable, 0, [], [],
                "No project in the solution matched the scope of this rule.");
        }

        var violations = new List<ConstraintFinding>();
        var unresolved = new List<ConstraintFinding>();
        string? reason = null;

        switch (rule.Kind)
        {
            case ConstraintRuleKind.ProhibitedPackages:
                foreach (var project in applicable)
                {
                    foreach (var package in project.PackageReferences.Where(p => rule.Packages.Contains(p.Name, StringComparer.OrdinalIgnoreCase)))
                        violations.Add(new ConstraintFinding(project.Name, $"references prohibited package {package.Name} {package.Version}"));
                }

                break;

            case ConstraintRuleKind.ApprovedPackages:
                foreach (var project in applicable)
                {
                    foreach (var package in project.PackageReferences.Where(p => !rule.Packages.Contains(p.Name, StringComparer.OrdinalIgnoreCase)))
                        violations.Add(new ConstraintFinding(project.Name, $"references unapproved package {package.Name} {package.Version}"));
                }

                break;

            case ConstraintRuleKind.MinimumPackageVersions:
                foreach (var project in applicable)
                {
                    foreach (var package in project.PackageReferences)
                    {
                        if (!rule.PackageVersions.TryGetValue(package.Name, out var minimum))
                            continue;

                        switch (CompareVersions(package.Version, minimum))
                        {
                            case < 0:
                                violations.Add(new ConstraintFinding(
                                    project.Name,
                                    $"{package.Name} {package.Version} is below the required minimum {minimum}"));
                                break;
                            case null:
                                unresolved.Add(new ConstraintFinding(
                                    project.Name,
                                    $"{package.Name} version '{package.Version}' cannot be compared to the required minimum {minimum}"));
                                break;
                        }
                    }
                }

                break;

            case ConstraintRuleKind.ApprovedTargetFrameworks:
                foreach (var project in applicable)
                {
                    if (string.Equals(project.TargetFramework, "unknown", StringComparison.OrdinalIgnoreCase))
                    {
                        unresolved.Add(new ConstraintFinding(project.Name, "target framework could not be determined"));
                    }
                    else if (!rule.Frameworks.Contains(project.TargetFramework, StringComparer.OrdinalIgnoreCase))
                    {
                        violations.Add(new ConstraintFinding(
                            project.Name,
                            $"targets {project.TargetFramework}, which is not an approved framework"));
                    }
                }

                break;

            case ConstraintRuleKind.ManualReview:
                reason = "This requirement is not decidable from static analysis and must be verified by a person.";
                foreach (var project in applicable)
                    unresolved.Add(new ConstraintFinding(project.Name, rule.Requirement ?? rule.Title));
                break;

            default:
                reason = $"Rule kind '{rule.DeclaredKind ?? "(unspecified)"}' is not implemented in this version of the framework.";
                unresolved.Add(new ConstraintFinding("(solution)", reason));
                break;
        }

        var verdict = violations.Count > 0
            ? ConstraintVerdict.NonCompliant
            : unresolved.Count > 0
                ? ConstraintVerdict.RequiresHumanReview
                : ConstraintVerdict.Compliant;

        return Result(rule, verdict, applicable.Count, violations, unresolved, reason);
    }

    private static ConstraintRuleResult Result(
        ConstraintRule rule,
        ConstraintVerdict verdict,
        int projectsEvaluated,
        IReadOnlyList<ConstraintFinding> violations,
        IReadOnlyList<ConstraintFinding> unresolved,
        string? reason) =>
        new()
        {
            RuleId = rule.Id,
            Title = rule.Title,
            Kind = rule.Kind,
            Verdict = verdict,
            ProjectsEvaluated = projectsEvaluated,
            Violations = violations,
            Unresolved = unresolved,
            Rationale = rule.Rationale,
            Reason = reason,
        };

    private static bool AppliesTo(ConstraintRule rule, string projectName)
    {
        if (rule.AppliesTo.Count == 0)
            return true;

        return rule.AppliesTo.Any(pattern =>
            Regex.IsMatch(
                projectName,
                "^" + Regex.Escape(pattern).Replace("\\*", ".*") + "$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant));
    }

    /// <summary>
    /// Compares two plain numeric versions. Returns null when either side is not a simple dotted
    /// numeric version — floating ranges, MSBuild properties and prerelease suffixes are reported for
    /// human review rather than ordered by guesswork.
    /// </summary>
    internal static int? CompareVersions(string left, string right)
    {
        if (!TryParse(left, out var leftParts) || !TryParse(right, out var rightParts))
            return null;

        for (var i = 0; i < Math.Max(leftParts.Count, rightParts.Count); i++)
        {
            var a = i < leftParts.Count ? leftParts[i] : 0;
            var b = i < rightParts.Count ? rightParts[i] : 0;
            if (a != b)
                return a.CompareTo(b);
        }

        return 0;

        static bool TryParse(string value, out List<int> parts)
        {
            parts = [];
            if (string.IsNullOrWhiteSpace(value) || !NumericVersion().IsMatch(value))
                return false;

            foreach (var segment in value.Split('.'))
                parts.Add(int.Parse(segment));

            return true;
        }
    }

    [GeneratedRegex(@"^\d+(\.\d+)*$")]
    private static partial Regex NumericVersion();
}
