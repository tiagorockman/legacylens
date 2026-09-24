using LegacyLens.Core;
using LegacyLens.Core.Analysis.Models;
using LegacyLens.Core.AppCat;
using LegacyLens.Core.Constraints.Models;
using LegacyLens.Core.Ingestion;
using LegacyLens.Core.Ingestion.Models;
using LegacyLens.Core.Reporting;

namespace LegacyLens.Core.Tests;

public sealed class BaselineReportGeneratorTests
{
    [Fact]
    public void GenerateMarkdown_ContainsAllSections()
    {
        var markdown = BaselineReportGenerator.GenerateMarkdown(CreateBaseline());

        Assert.Contains("# Solution Baseline Report", markdown);
        Assert.Contains("## Projects", markdown);
        Assert.Contains("## Project Dependency Graph", markdown);
        Assert.Contains("```mermaid", markdown);
        Assert.Contains("Web[\"Web\"] --> Domain[\"Domain\"]", markdown);
        Assert.Contains("## Complexity Hotspots", markdown);
        Assert.Contains("Domain.OrderService.Price(int)", markdown);
        Assert.Contains("## Deprecated Symbols", markdown);
        Assert.Contains("Domain.OrderService.OldPrice(int)", markdown);
        Assert.Contains("## AppCAT Incidents", markdown);
        Assert.Contains("| Mandatory | 2 |", markdown);
    }

    [Fact]
    public void GenerateMarkdown_WithConstraints_ReportsEveryVerdictAndItsFindings()
    {
        var evaluation = new ConstraintEvaluation
        {
            Policy = new ConstraintPolicy
            {
                SchemaVersion = 1,
                Organization = "Contoso Ltd.",
                Rules = [],
                SourceName = "policy.json",
            },
            Results =
            [
                new ConstraintRuleResult
                {
                    RuleId = "PKG-001",
                    Title = "No legacy EF",
                    Kind = ConstraintRuleKind.ProhibitedPackages,
                    Verdict = ConstraintVerdict.NonCompliant,
                    ProjectsEvaluated = 2,
                    Violations = [new ConstraintFinding("Web", "references prohibited package EntityFramework 6.4.4")],
                    Unresolved = [],
                },
                new ConstraintRuleResult
                {
                    RuleId = "SEC-001",
                    Title = "OAuth",
                    Kind = ConstraintRuleKind.ManualReview,
                    Verdict = ConstraintVerdict.RequiresHumanReview,
                    ProjectsEvaluated = 2,
                    Violations = [],
                    Unresolved = [new ConstraintFinding("Web", "Confirm OAuth 2.0.")],
                },
            ],
        };

        var markdown = BaselineReportGenerator.GenerateMarkdown(CreateBaseline() with { Constraints = evaluation });

        Assert.Contains("## Organization Constraints", markdown);
        Assert.Contains("Contoso Ltd.", markdown);
        Assert.Contains("❌ Non-compliant", markdown);
        Assert.Contains("🔍 Requires human review", markdown);
        Assert.Contains("references prohibited package EntityFramework 6.4.4", markdown);
        Assert.Contains("Confirm OAuth 2.0.", markdown);
    }

    [Fact]
    public void GenerateMarkdown_WithoutConstraints_OmitsTheSection() =>
        Assert.DoesNotContain("## Organization Constraints", BaselineReportGenerator.GenerateMarkdown(CreateBaseline()));

    [Fact]
    public void GenerateMarkdown_WithoutAppCat_ExplainsHowToSupplyReport()
    {
        var markdown = BaselineReportGenerator.GenerateMarkdown(CreateBaseline() with { AppCat = null });

        Assert.Contains("No AppCAT report supplied", markdown);
    }

    private static BaselineResult CreateBaseline()
    {
        var projects = new[]
        {
            new ProjectInfo
            {
                Name = "Web",
                FilePath = "Web/Web.csproj",
                TargetFramework = ".NET Framework v4.7.2",
                ProjectReferences = ["Domain"],
                PackageReferences = [new PackageReferenceInfo("EntityFramework", "6.4.4")],
                DocumentCount = 42,
            },
            new ProjectInfo
            {
                Name = "Domain",
                FilePath = "Domain/Domain.csproj",
                TargetFramework = ".NET Framework v4.7.2",
                ProjectReferences = [],
                PackageReferences = [],
                DocumentCount = 17,
            },
        };

        var symbols = new List<ProjectSymbols>
        {
            new("Domain", 0,
            [
                new SymbolRecord
                {
                    ProjectName = "Domain",
                    FullyQualifiedName = "Domain.OrderService",
                    Kind = SymbolKind.Type,
                    FilePath = "Domain/OrderService.cs",
                    Complexity = 0,
                    IsDeprecated = false,
                },
                new SymbolRecord
                {
                    ProjectName = "Domain",
                    FullyQualifiedName = "Domain.OrderService.Price(int)",
                    Kind = SymbolKind.Method,
                    FilePath = "Domain/OrderService.cs",
                    Complexity = 7,
                    IsDeprecated = false,
                },
                new SymbolRecord
                {
                    ProjectName = "Domain",
                    FullyQualifiedName = "Domain.OrderService.OldPrice(int)",
                    Kind = SymbolKind.Method,
                    FilePath = "Domain/OrderService.cs",
                    Complexity = 2,
                    IsDeprecated = true,
                },
            ]),
        };

        var appCat = new AppCatReport("report.json",
        [
            new AppCatIncident("Api.WebForms", "Mandatory", "Web Forms unavailable", "Default.aspx", 12, "Web", 3),
            new AppCatIncident("Binary.Framework", "Mandatory", null, null, null, "Web", 3),
            new AppCatIncident("Config.Tls", "Potential", "TLS 1.0", "web.config", null, "Web", 1),
        ]);

        return new BaselineResult
        {
            RunId = "test-run",
            SolutionPath = "Sample.sln",
            Graph = ProjectGraph.Build(projects),
            ProjectSymbols = symbols,
            AppCat = appCat,
            WorkspaceDiagnostics = [],
            Elapsed = TimeSpan.FromSeconds(12.5),
            ReportPath = "out/solution-baseline.md",
            DatabasePath = "out/legacylens.db",
        };
    }
}
