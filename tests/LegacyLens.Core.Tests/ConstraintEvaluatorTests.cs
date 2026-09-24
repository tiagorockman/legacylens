using LegacyLens.Core.Constraints;
using LegacyLens.Core.Constraints.Models;
using LegacyLens.Core.Ingestion.Models;

namespace LegacyLens.Core.Tests;

public sealed class ConstraintEvaluatorTests
{
    private static readonly IReadOnlyList<ProjectInfo> Solution =
    [
        Project("Billing.Core", ".NET Framework v4.7.2", ("EntityFramework", "6.4.4"), ("Newtonsoft.Json", "9.0.1")),
        Project("Billing.Web", "net8.0", ("Newtonsoft.Json", "13.0.3")),
    ];

    [Fact]
    public void Evaluate_ProhibitedPackageReferenced_IsNonCompliant()
    {
        var result = Single(Rule(ConstraintRuleKind.ProhibitedPackages) with { Packages = ["EntityFramework"] });

        Assert.Equal(ConstraintVerdict.NonCompliant, result.Verdict);
        var violation = Assert.Single(result.Violations);
        Assert.Equal("Billing.Core", violation.Project);
        Assert.Contains("EntityFramework 6.4.4", violation.Detail);
        Assert.Equal(2, result.ProjectsEvaluated);
    }

    [Fact]
    public void Evaluate_ProhibitedPackageAbsent_IsCompliant()
    {
        var result = Single(Rule(ConstraintRuleKind.ProhibitedPackages) with { Packages = ["log4net"] });

        Assert.Equal(ConstraintVerdict.Compliant, result.Verdict);
        Assert.Empty(result.Violations);
        Assert.Empty(result.Unresolved);
    }

    [Fact]
    public void Evaluate_RuleScopedToAbsentProjects_IsNotApplicable()
    {
        var result = Single(Rule(ConstraintRuleKind.ProhibitedPackages) with
        {
            AppliesTo = ["*.Api"],
            Packages = ["EntityFramework"],
        });

        Assert.Equal(ConstraintVerdict.NotApplicable, result.Verdict);
        Assert.Equal(0, result.ProjectsEvaluated);
        Assert.NotNull(result.Reason);
    }

    [Fact]
    public void Evaluate_ScopePatternMatchesSubsetOfProjects()
    {
        var result = Single(Rule(ConstraintRuleKind.ProhibitedPackages) with
        {
            AppliesTo = ["*.Web"],
            Packages = ["EntityFramework"],
        });

        // Only Billing.Web is in scope, and it does not reference the prohibited package.
        Assert.Equal(ConstraintVerdict.Compliant, result.Verdict);
        Assert.Equal(1, result.ProjectsEvaluated);
    }

    [Fact]
    public void Evaluate_PackageBelowMinimumVersion_IsNonCompliantAndNamesBothVersions()
    {
        var result = Single(Rule(ConstraintRuleKind.MinimumPackageVersions) with
        {
            PackageVersions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Newtonsoft.Json"] = "13.0.1",
            },
        });

        Assert.Equal(ConstraintVerdict.NonCompliant, result.Verdict);
        var violation = Assert.Single(result.Violations);
        Assert.Equal("Billing.Core", violation.Project);
        Assert.Contains("9.0.1", violation.Detail);
        Assert.Contains("13.0.1", violation.Detail);
    }

    [Fact]
    public void Evaluate_UncomparablePackageVersion_RequiresHumanReview()
    {
        IReadOnlyList<ProjectInfo> solution = [Project("Legacy.App", "net8.0", ("Serilog", "$(SerilogVersion)"))];
        var rule = Rule(ConstraintRuleKind.MinimumPackageVersions) with
        {
            PackageVersions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Serilog"] = "3.0.0" },
        };

        var result = Assert.Single(ConstraintEvaluator.Evaluate(Policy(rule), solution).Results);

        Assert.Equal(ConstraintVerdict.RequiresHumanReview, result.Verdict);
        Assert.Empty(result.Violations);
        Assert.Contains("cannot be compared", Assert.Single(result.Unresolved).Detail);
    }

    [Fact]
    public void Evaluate_UnapprovedTargetFramework_IsNonCompliant()
    {
        var result = Single(Rule(ConstraintRuleKind.ApprovedTargetFrameworks) with { Frameworks = ["net8.0", "net10.0"] });

        Assert.Equal(ConstraintVerdict.NonCompliant, result.Verdict);
        var violation = Assert.Single(result.Violations);
        Assert.Equal("Billing.Core", violation.Project);
        Assert.Contains(".NET Framework v4.7.2", violation.Detail);
    }

    [Fact]
    public void Evaluate_UndeterminedTargetFramework_RequiresHumanReview()
    {
        IReadOnlyList<ProjectInfo> solution = [Project("Mystery", "unknown")];
        var rule = Rule(ConstraintRuleKind.ApprovedTargetFrameworks) with { Frameworks = ["net8.0"] };

        var result = Assert.Single(ConstraintEvaluator.Evaluate(Policy(rule), solution).Results);

        Assert.Equal(ConstraintVerdict.RequiresHumanReview, result.Verdict);
    }

    [Fact]
    public void Evaluate_ManualReviewRule_NeverPassesSilently()
    {
        var result = Single(Rule(ConstraintRuleKind.ManualReview) with
        {
            Requirement = "Authentication must use OAuth 2.0.",
        });

        Assert.Equal(ConstraintVerdict.RequiresHumanReview, result.Verdict);
        Assert.Equal(2, result.Unresolved.Count);
        Assert.All(result.Unresolved, f => Assert.Equal("Authentication must use OAuth 2.0.", f.Detail));
    }

    [Fact]
    public void Evaluate_UnsupportedRuleKind_RequiresHumanReviewAndNamesTheKind()
    {
        var result = Single(Rule(ConstraintRuleKind.Unsupported) with { DeclaredKind = "architecturalBoundaries" });

        Assert.Equal(ConstraintVerdict.RequiresHumanReview, result.Verdict);
        Assert.Contains("architecturalBoundaries", result.Reason);
    }

    [Theory]
    [InlineData("1.0.0", "1.0.0", 0)]
    [InlineData("2.0", "1.9.9", 1)]
    [InlineData("6.2.1", "10.0.0", -1)]
    [InlineData("13.0", "13.0.0", 0)]
    public void CompareVersions_OrdersPlainNumericVersions(string left, string right, int expected) =>
        Assert.Equal(expected, Math.Sign(ConstraintEvaluator.CompareVersions(left, right)!.Value));

    [Theory]
    [InlineData("4.5.0-beta")]
    [InlineData("*")]
    [InlineData("$(Version)")]
    [InlineData("")]
    public void CompareVersions_RefusesToGuessOnNonNumericVersions(string version) =>
        Assert.Null(ConstraintEvaluator.CompareVersions(version, "1.0.0"));

    private static ConstraintRuleResult Single(ConstraintRule rule) =>
        Assert.Single(ConstraintEvaluator.Evaluate(Policy(rule), Solution).Results);

    private static ConstraintPolicy Policy(ConstraintRule rule) => new()
    {
        SchemaVersion = ConstraintPolicy.CurrentSchemaVersion,
        Organization = "Test Organization",
        Rules = [rule],
        SourceName = "policy.json",
    };

    private static ConstraintRule Rule(ConstraintRuleKind kind) => new()
    {
        Id = "RULE-001",
        Title = "Test rule",
        Kind = kind,
    };

    private static ProjectInfo Project(string name, string targetFramework, params (string Name, string Version)[] packages) => new()
    {
        Name = name,
        FilePath = $"{name}/{name}.csproj",
        TargetFramework = targetFramework,
        ProjectReferences = [],
        PackageReferences = packages.Select(p => new PackageReferenceInfo(p.Name, p.Version)).ToList(),
        DocumentCount = 1,
    };
}
