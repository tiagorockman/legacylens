using System.Diagnostics;
using LegacyLens.Core.AppCat;
using LegacyLens.Core.Ingestion;
using LegacyLens.Core.Tracing;
using LegacyLens.Core.Tracing.Models;

namespace LegacyLens.Core;

public sealed record TraceRunOptions
{
    public required string SolutionPath { get; init; }

    /// <summary>Optional `dotnet appcat` JSON export whose incidents are mapped onto traced methods.</summary>
    public string? AppCatReportPath { get; init; }

    public required string OutputDirectory { get; init; }

    public TraceSelection Selection { get; init; } = new();

    public int TokenBudget { get; init; } = TraceBuilder.DefaultTokenBudget;
}

public sealed record TraceRunResult
{
    public required IReadOnlyList<TraceFile> Traces { get; init; }

    public required IReadOnlyList<string> ProjectsNeedingRestore { get; init; }

    public required TimeSpan Elapsed { get; init; }

    public required string IndexPath { get; init; }

    public required string TracesDirectory { get; init; }
}

/// <summary>
/// Layer 3 entry point: load → select → trace → write. Produces the compiler-verified method traces
/// that ground business-rule extraction and transformation prompts.
/// </summary>
public sealed class TraceRunner
{
    private const int MaxProgressNameLength = 120;

    public async Task<TraceRunResult> RunAsync(
        TraceRunOptions options,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        using var analysis = await new ProjectGraphLoader().LoadAsync(options.SolutionPath, progress, cancellationToken);
        var solutionName = Path.GetFileName(analysis.SolutionPath);

        var needingRestore = analysis.Graph.Projects.Where(p => p.NeedsPackageRestore).Select(p => p.Name).ToList();
        if (needingRestore.Count > 0)
        {
            progress?.Report(
                $"WARNING: {needingRestore.Count} project(s) use packages.config but are not restored, so their " +
                $"traces will be partially resolved. Run: nuget restore \"{solutionName}\"");
        }

        AppCatReport? appCat = null;
        if (options.AppCatReportPath is not null)
        {
            progress?.Report($"Ingesting AppCAT report {Path.GetFileName(options.AppCatReportPath)}");
            appCat = AppCatReportParser.Parse(options.AppCatReportPath);
        }

        progress?.Report("Selecting methods to trace");
        var targets = await TraceTargetSelector.SelectAsync(
            analysis.Solution,
            analysis.Graph,
            options.Selection,
            cancellationToken);

        var builder = new TraceBuilder(
            analysis.Solution,
            Path.GetDirectoryName(analysis.SolutionPath)!,
            appCat,
            options.TokenBudget);

        var traces = new List<MethodTrace>(targets.Count);
        for (var i = 0; i < targets.Count; i++)
        {
            var target = targets[i];
            var name = target.FullyQualifiedName.Length <= MaxProgressNameLength
                ? target.FullyQualifiedName
                : target.FullyQualifiedName[..MaxProgressNameLength] + "…";
            progress?.Report($"  [{i + 1}/{targets.Count}] {name} (complexity {target.Complexity})");

            traces.Add(await builder.BuildAsync(target.Project, target.Method, cancellationToken));
        }

        var outputDirectory = Path.GetFullPath(options.OutputDirectory);
        progress?.Report("Writing traces");
        var files = await TraceOutputWriter.WriteTracesAsync(outputDirectory, traces, cancellationToken);

        stopwatch.Stop();
        var indexPath = await TraceOutputWriter.WriteIndexAsync(
            outputDirectory,
            solutionName,
            options.Selection,
            options.TokenBudget,
            appCat is not null,
            files,
            stopwatch.Elapsed,
            cancellationToken);

        return new TraceRunResult
        {
            Traces = files,
            ProjectsNeedingRestore = needingRestore,
            Elapsed = stopwatch.Elapsed,
            IndexPath = indexPath,
            TracesDirectory = Path.Combine(outputDirectory, TraceOutputWriter.TracesFolder),
        };
    }
}
