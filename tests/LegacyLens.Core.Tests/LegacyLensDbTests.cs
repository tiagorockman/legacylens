using LegacyLens.Core;
using LegacyLens.Core.Analysis.Models;
using LegacyLens.Core.Ingestion;
using LegacyLens.Core.Ingestion.Models;
using LegacyLens.Core.Persistence;
using Microsoft.Data.Sqlite;

namespace LegacyLens.Core.Tests;

public sealed class LegacyLensDbTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"legacylens-{Guid.NewGuid():N}.db");

    [Fact]
    public void SaveBaseline_PersistsProjectsSymbolsAndReportRow()
    {
        var baseline = CreateBaseline();

        using (var db = new LegacyLensDb(_dbPath))
        {
            db.SaveBaseline(baseline);
        }

        Assert.Equal(1, Scalar("SELECT COUNT(*) FROM projects"));
        Assert.Equal(2, Scalar("SELECT COUNT(*) FROM symbols"));
        Assert.Equal(1, Scalar("SELECT COUNT(*) FROM reports"));
        Assert.Equal(1, Scalar("SELECT COUNT(*) FROM symbols WHERE is_deprecated = 1"));
        Assert.Equal(1, Scalar("SELECT COUNT(*) FROM projects WHERE build_status = 'OK'"));
    }

    private long Scalar(string sql)
    {
        using var connection = new SqliteConnection($"Data Source={_dbPath}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return (long)command.ExecuteScalar()!;
    }

    private static BaselineResult CreateBaseline()
    {
        var project = new ProjectInfo
        {
            Name = "Domain",
            FilePath = "Domain/Domain.csproj",
            TargetFramework = "net8.0",
            ProjectReferences = [],
            PackageReferences = [new PackageReferenceInfo("Newtonsoft.Json", "13.0.3")],
            DocumentCount = 3,
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
                    FullyQualifiedName = "Domain.OrderService.OldPrice(int)",
                    Kind = SymbolKind.Method,
                    FilePath = null,
                    Complexity = 2,
                    IsDeprecated = true,
                },
            ]),
        };

        return new BaselineResult
        {
            RunId = "test-run",
            SolutionPath = "Sample.sln",
            Graph = ProjectGraph.Build([project]),
            ProjectSymbols = symbols,
            AppCat = null,
            WorkspaceDiagnostics = [],
            Elapsed = TimeSpan.FromSeconds(3),
            ReportPath = "out/solution-baseline.md",
            DatabasePath = "out/legacylens.db",
        };
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath))
            File.Delete(_dbPath);
    }
}
