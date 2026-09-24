using System.Diagnostics;
using LegacyLens.Core.Analysis;
using LegacyLens.Core.Analysis.Models;
using LegacyLens.Core.AppCat;
using LegacyLens.Core.Constraints;
using LegacyLens.Core.Constraints.Models;
using LegacyLens.Core.Ingestion;
using LegacyLens.Core.Persistence;
using LegacyLens.Core.Reporting;

namespace LegacyLens.Core;

public sealed record BaselineOptions
{
    public required string SolutionPath { get; init; }

    /// <summary>Optional path to a `dotnet appcat` JSON export; the baseline omits the AppCAT section when absent.</summary>
    public string? AppCatReportPath { get; init; }

    public required string OutputDirectory { get; init; }

    /// <summary>Optional organization constraint policy (JSON) to evaluate the solution against.</summary>
    public string? ConstraintsPath { get; init; }

    public int TopComplexMethods { get; init; } = 20;
}

public sealed record BaselineResult
{
    public required string RunId { get; init; }

    public required string SolutionPath { get; init; }

    public required ProjectGraph Graph { get; init; }

    public required IReadOnlyList<ProjectSymbols> ProjectSymbols { get; init; }

    public required AppCatReport? AppCat { get; init; }

    /// <summary>Result of evaluating the organization constraint policy, when one was supplied.</summary>
    public ConstraintEvaluation? Constraints { get; init; }

    public required IReadOnlyList<string> WorkspaceDiagnostics { get; init; }

    public required TimeSpan Elapsed { get; init; }

    public required string ReportPath { get; init; }

    public required string DatabasePath { get; init; }
}

/// <summary>
/// Phase 1 entry point: load → analyze → persist → report. Produces the
/// solution-baseline.md report and the legacylens.db SQLite snapshot.
/// </summary>
public sealed class AnalysisRunner
{
    public async Task<BaselineResult> RunAsync(
        BaselineOptions options,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var runId = $"{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N")[..8]}";

        var loader = new ProjectGraphLoader();
        using var analysis = await loader.LoadAsync(options.SolutionPath, progress, cancellationToken);

        var extractor = new SymbolExtractor();
        var symbols = new List<ProjectSymbols>();
        var seenProjects = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var project in analysis.Solution.Projects)
        {
            // Multi-targeted projects compile once per TFM; index the first flavor only.
            if (project.FilePath is not null && !seenProjects.Add(project.FilePath))
                continue;

            progress?.Report($"  Extracting symbols from {project.Name}");
            symbols.Add(await extractor.ExtractAsync(project, cancellationToken));
        }

        AppCatReport? appCat = null;
        if (options.AppCatReportPath is not null)
        {
            progress?.Report($"Ingesting AppCAT report {Path.GetFileName(options.AppCatReportPath)}");
            appCat = AppCatReportParser.Parse(options.AppCatReportPath);
        }

        ConstraintEvaluation? constraints = null;
        if (options.ConstraintsPath is not null)
        {
            progress?.Report($"Evaluating organization constraints from {Path.GetFileName(options.ConstraintsPath)}");
            var policy = ConstraintPolicyParser.Parse(options.ConstraintsPath);
            constraints = ConstraintEvaluator.Evaluate(policy, analysis.Graph.Projects);
        }

        var outputDirectory = Path.GetFullPath(options.OutputDirectory);
        Directory.CreateDirectory(outputDirectory);
        var reportPath = Path.Combine(outputDirectory, "solution-baseline.md");
        var databasePath = Path.Combine(outputDirectory, "legacylens.db");

        stopwatch.Stop();
        var result = new BaselineResult
        {
            RunId = runId,
            SolutionPath = analysis.SolutionPath,
            Graph = analysis.Graph,
            ProjectSymbols = symbols,
            AppCat = appCat,
            Constraints = constraints,
            WorkspaceDiagnostics = analysis.WorkspaceDiagnostics,
            Elapsed = stopwatch.Elapsed,
            ReportPath = reportPath,
            DatabasePath = databasePath,
        };

        progress?.Report("Persisting baseline to SQLite");
        using (var db = new LegacyLensDb(databasePath))
        {
            db.SaveBaseline(result);
        }

        progress?.Report("Writing solution-baseline.md");
        await File.WriteAllTextAsync(
            reportPath,
            BaselineReportGenerator.GenerateMarkdown(result, options.TopComplexMethods),
            cancellationToken);

        return result;
    }
}
