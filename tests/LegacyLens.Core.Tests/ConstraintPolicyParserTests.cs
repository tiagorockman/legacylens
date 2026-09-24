using LegacyLens.Core.Constraints;
using LegacyLens.Core.Constraints.Models;

namespace LegacyLens.Core.Tests;

public sealed class ConstraintPolicyParserTests : IDisposable
{
    private readonly string _tempFile = Path.Combine(Path.GetTempPath(), $"policy-{Guid.NewGuid():N}.json");

    [Fact]
    public void Parse_ReadsPolicyMetadataAndEveryKnownRuleKind()
    {
        File.WriteAllText(_tempFile,
            """
            {
              "schemaVersion": 1,
              "organization": "Contoso Ltd.",
              "description": "Modernization constraints",
              "rules": [
                { "id": "PKG-001", "title": "No legacy EF", "kind": "prohibitedPackages", "packages": ["EntityFramework"] },
                { "id": "PKG-002", "title": "Allowlist", "kind": "approvedPackages", "packages": ["Serilog"] },
                { "id": "PKG-003", "title": "Minimums", "kind": "minimumPackageVersions",
                  "packageVersions": { "Newtonsoft.Json": "13.0.1" } },
                { "id": "TFM-001", "title": "Supported runtimes", "kind": "approvedTargetFrameworks",
                  "frameworks": ["net8.0"], "appliesTo": ["*.Web"] },
                { "id": "SEC-001", "title": "OAuth", "kind": "manualReview", "requirement": "Verify OAuth 2.0." }
              ]
            }
            """);

        var policy = ConstraintPolicyParser.Parse(_tempFile);

        Assert.Equal(1, policy.SchemaVersion);
        Assert.Equal("Contoso Ltd.", policy.Organization);
        Assert.Equal(Path.GetFileName(_tempFile), policy.SourceName);
        Assert.Equal(
            new[]
            {
                ConstraintRuleKind.ProhibitedPackages,
                ConstraintRuleKind.ApprovedPackages,
                ConstraintRuleKind.MinimumPackageVersions,
                ConstraintRuleKind.ApprovedTargetFrameworks,
                ConstraintRuleKind.ManualReview,
            },
            policy.Rules.Select(r => r.Kind));

        Assert.Equal("EntityFramework", Assert.Single(policy.Rules[0].Packages));
        Assert.Equal("13.0.1", policy.Rules[2].PackageVersions["Newtonsoft.Json"]);
        Assert.Equal("*.Web", Assert.Single(policy.Rules[3].AppliesTo));
        Assert.Equal("Verify OAuth 2.0.", policy.Rules[4].Requirement);
    }

    [Fact]
    public void Parse_UnknownRuleKindIsPreservedRatherThanDropped()
    {
        File.WriteAllText(_tempFile,
            """
            {
              "rules": [
                { "id": "ARCH-001", "title": "Layering", "kind": "architecturalBoundaries" }
              ]
            }
            """);

        var rule = Assert.Single(ConstraintPolicyParser.Parse(_tempFile).Rules);

        Assert.Equal(ConstraintRuleKind.Unsupported, rule.Kind);
        Assert.Equal("architecturalBoundaries", rule.DeclaredKind);
    }

    [Fact]
    public void Parse_SuppliesIdentifiersForRulesThatOmitThem()
    {
        File.WriteAllText(_tempFile, """{ "rules": [ { "kind": "manualReview" } ] }""");

        var rule = Assert.Single(ConstraintPolicyParser.Parse(_tempFile).Rules);

        Assert.Equal("RULE-001", rule.Id);
        Assert.Equal("manualReview", rule.Title);
        Assert.Equal(ConstraintPolicy.CurrentSchemaVersion, ConstraintPolicyParser.Parse(_tempFile).SchemaVersion);
    }

    [Fact]
    public void Parse_EmptyPolicyYieldsNoRules() =>
        Assert.Empty(WriteAndParse("{}").Rules);

    private ConstraintPolicy WriteAndParse(string json)
    {
        File.WriteAllText(_tempFile, json);
        return ConstraintPolicyParser.Parse(_tempFile);
    }

    public void Dispose()
    {
        if (File.Exists(_tempFile))
            File.Delete(_tempFile);
    }
}
