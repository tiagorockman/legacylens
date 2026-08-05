using LegacyLens.Core;
using LegacyLens.Core.Analysis.Models;
using LegacyLens.Core.AppCat;
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
