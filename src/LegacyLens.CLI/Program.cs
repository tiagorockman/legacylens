using System.CommandLine;
using LegacyLens.Core;
using LegacyLens.Core.Analysis.Models;

var solutionOption = new Option<FileInfo>("--solution", "-s")
{
    Description = "Path to the .sln file to analyze.",
    Required = true,
};

var appcatOption = new Option<FileInfo?>("--appcat")
{
    Description = "Optional path to a `dotnet appcat` JSON export to map incidents into the baseline.",
};

var outputOption = new Option<DirectoryInfo>("--output", "-o")
{
    Description = "Directory for solution-baseline.md and legacylens.db.",
    DefaultValueFactory = _ => new DirectoryInfo("legacylens-out"),
};

var analyzeCommand = new Command("analyze", "Run the Phase 1 deterministic baseline analysis on a .NET solution.")
{
    solutionOption,
    appcatOption,
    outputOption,
};

analyzeCommand.SetAction(async (parseResult, cancellationToken) =>
{
    var solution = parseResult.GetValue(solutionOption)!;
    var appcat = parseResult.GetValue(appcatOption);
    var output = parseResult.GetValue(outputOption)!;

    if (!solution.Exists)
    {
        Console.Error.WriteLine($"error: solution file not found: {solution.FullName}");
        return 2;
    }

    if (appcat is not null && !appcat.Exists)
    {
        Console.Error.WriteLine($"error: AppCAT report not found: {appcat.FullName}");
        return 2;
    }

    var progress = new Progress<string>(message =>
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {message}"));

    try
    {
        var runner = new AnalysisRunner();
        var result = await runner.RunAsync(
            new BaselineOptions
            {
                SolutionPath = solution.FullName,
                AppCatReportPath = appcat?.FullName,
                OutputDirectory = output.FullName,
            },
            progress,
            cancellationToken);

        var types = result.ProjectSymbols.Sum(p => p.Symbols.Count(s => s.Kind == SymbolKind.Type));
        var methods = result.ProjectSymbols.Sum(p => p.Symbols.Count(s => s.Kind == SymbolKind.Method));

        Console.WriteLine();
        Console.WriteLine($"LegacyLens baseline complete in {result.Elapsed.TotalSeconds:F1}s");
        Console.WriteLine($"  Projects : {result.Graph.Projects.Count} ({result.Graph.Cycles.Count} cycle(s))");
        Console.WriteLine($"  Symbols  : {types} types, {methods} methods");
        if (result.AppCat is not null)
            Console.WriteLine($"  AppCAT   : {result.AppCat.Incidents.Count} incident(s)");
        Console.WriteLine($"  Report   : {result.ReportPath}");
        Console.WriteLine($"  Database : {result.DatabasePath}");
        return 0;
    }
    catch (OperationCanceledException)
    {
        Console.Error.WriteLine("Analysis canceled.");
        return 130;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"error: {ex.Message}");
        return 1;
    }
});

var rootCommand = new RootCommand(
    "LegacyLens — AI-assisted .NET legacy modernization agent (Phase 1: deterministic core).")
{
    analyzeCommand,
};

return await rootCommand.Parse(args).InvokeAsync();
