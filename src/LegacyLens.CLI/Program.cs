using System.CommandLine;
using LegacyLens.Core;
using LegacyLens.Core.Analysis.Models;
using LegacyLens.Core.Constraints.Models;
using LegacyLens.Core.Tracing;

static Option<FileInfo> SolutionOption() => new("--solution", "-s")
{
    Description = "Path to the .sln file to analyze.",
    Required = true,
};

static Option<FileInfo?> AppCatOption() => new("--appcat")
{
    Description = "Optional path to a `dotnet appcat` JSON export to map incidents into the output.",
};

static Option<FileInfo?> ConstraintsOption() => new("--constraints")
{
    Description = "Optional organization constraint policy (JSON) to evaluate the solution against.",
};

static Option<DirectoryInfo> OutputOption() => new("--output", "-o")
{
    Description = "Output directory.",
    DefaultValueFactory = _ => new DirectoryInfo("legacylens-out"),
};

static int? ValidateInputs(FileInfo solution, FileInfo? appcat)
{
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

    return null;
}

static IProgress<string> ConsoleProgress() =>
    new Progress<string>(message => Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {message}"));

static async Task<int> RunSafelyAsync(Func<Task<int>> action)
{
    try
    {
        return await action();
    }
    catch (OperationCanceledException)
    {
        Console.Error.WriteLine("Canceled.");
        return 130;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"error: {ex.Message}");
        return 1;
    }
}

static Command CreateAnalyzeCommand()
{
    var solutionOption = SolutionOption();
    var appcatOption = AppCatOption();
    var constraintsOption = ConstraintsOption();
    var outputOption = OutputOption();

    var command = new Command("analyze", "Run the deterministic baseline analysis on a .NET solution.")
    {
        solutionOption,
        appcatOption,
        constraintsOption,
        outputOption,
    };

    command.SetAction((parseResult, cancellationToken) => RunSafelyAsync(async () =>
    {
        var solution = parseResult.GetValue(solutionOption)!;
        var appcat = parseResult.GetValue(appcatOption);
        var constraints = parseResult.GetValue(constraintsOption);
        var output = parseResult.GetValue(outputOption)!;
        if (ValidateInputs(solution, appcat) is int invalid)
            return invalid;

        if (constraints is not null && !constraints.Exists)
        {
            Console.Error.WriteLine($"error: constraint policy not found: {constraints.FullName}");
            return 2;
        }

        var result = await new AnalysisRunner().RunAsync(
            new BaselineOptions
            {
                SolutionPath = solution.FullName,
                AppCatReportPath = appcat?.FullName,
                ConstraintsPath = constraints?.FullName,
                OutputDirectory = output.FullName,
            },
            ConsoleProgress(),
            cancellationToken);

        var types = result.ProjectSymbols.Sum(p => p.Symbols.Count(s => s.Kind == SymbolKind.Type));
        var methods = result.ProjectSymbols.Sum(p => p.Symbols.Count(s => s.Kind == SymbolKind.Method));

        Console.WriteLine();
        Console.WriteLine($"LegacyLens baseline complete in {result.Elapsed.TotalSeconds:F1}s");
        Console.WriteLine($"  Projects : {result.Graph.Projects.Count} ({result.Graph.Cycles.Count} cycle(s))");
        Console.WriteLine($"  Symbols  : {types} types, {methods} methods");
        if (result.AppCat is not null)
            Console.WriteLine($"  AppCAT   : {result.AppCat.Incidents.Count} incident(s)");
        if (result.Constraints is { } policy)
        {
            Console.WriteLine(
                $"  Policy   : {policy.Results.Count} rule(s) — " +
                $"{policy.Count(ConstraintVerdict.Compliant)} compliant, " +
                $"{policy.Count(ConstraintVerdict.NonCompliant)} non-compliant, " +
                $"{policy.Count(ConstraintVerdict.RequiresHumanReview)} need review, " +
                $"{policy.Count(ConstraintVerdict.NotApplicable)} not applicable");
        }
        Console.WriteLine($"  Report   : {result.ReportPath}");
        Console.WriteLine($"  Database : {result.DatabasePath}");
        return 0;
    }));

    return command;
}

static Command CreateTraceCommand()
{
    var solutionOption = SolutionOption();
    var appcatOption = AppCatOption();
    var outputOption = OutputOption();
    var methodOption = new Option<string?>("--method", "-m")
    {
        Description = "Trace only methods whose fully-qualified name contains this text (case-insensitive).",
    };
    var projectOption = new Option<string?>("--project", "-p")
    {
        Description = "Trace only methods declared in this project.",
    };
    var topOption = new Option<int>("--top")
    {
        Description = "Maximum number of methods to trace, most complex first.",
        DefaultValueFactory = _ => 20,
    };
    var minComplexityOption = new Option<int>("--min-complexity")
    {
        Description = "Skip methods below this cyclomatic complexity.",
        DefaultValueFactory = _ => 1,
    };
    var includeTestsOption = new Option<bool>("--include-tests")
    {
        Description = "Also trace methods in test projects.",
    };
    var tokenBudgetOption = new Option<int>("--token-budget")
    {
        Description = "Maximum estimated tokens per source chunk before a method is split.",
        DefaultValueFactory = _ => TraceBuilder.DefaultTokenBudget,
    };

    var command = new Command(
        "trace",
        "Build compiler-verified method traces (Layer 3) that ground LLM analysis in resolved types and call edges.")
    {
        solutionOption,
        appcatOption,
        outputOption,
        methodOption,
        projectOption,
        topOption,
        minComplexityOption,
        includeTestsOption,
        tokenBudgetOption,
    };

    command.SetAction((parseResult, cancellationToken) => RunSafelyAsync(async () =>
    {
        var solution = parseResult.GetValue(solutionOption)!;
        var appcat = parseResult.GetValue(appcatOption);
        var output = parseResult.GetValue(outputOption)!;
        if (ValidateInputs(solution, appcat) is int invalid)
            return invalid;

        var top = parseResult.GetValue(topOption);
        var tokenBudget = parseResult.GetValue(tokenBudgetOption);
        if (top <= 0 || tokenBudget <= 0)
        {
            Console.Error.WriteLine("error: --top and --token-budget must be greater than zero.");
            return 2;
        }

        var result = await new TraceRunner().RunAsync(
            new TraceRunOptions
            {
                SolutionPath = solution.FullName,
                AppCatReportPath = appcat?.FullName,
                OutputDirectory = output.FullName,
                TokenBudget = tokenBudget,
                Selection = new TraceSelection
                {
                    MethodFilter = parseResult.GetValue(methodOption),
                    ProjectFilter = parseResult.GetValue(projectOption),
                    Top = top,
                    MinComplexity = parseResult.GetValue(minComplexityOption),
                    IncludeTestProjects = parseResult.GetValue(includeTestsOption),
                },
            },
            ConsoleProgress(),
            cancellationToken);

        var traces = result.Traces.Select(f => f.Trace).ToList();

        Console.WriteLine();
        Console.WriteLine($"LegacyLens trace complete in {result.Elapsed.TotalSeconds:F1}s");
        Console.WriteLine(
            $"  Traced   : {traces.Count} method(s), " +
            $"{traces.Count(t => t.Grounding.IsFullyResolved)} fully resolved by the compiler");
        Console.WriteLine($"  Chunks   : {traces.Sum(t => t.Chunks.Count)} (~{traces.Sum(t => t.EstimatedTokens):N0} tokens)");
        Console.WriteLine($"  Index    : {result.IndexPath}");
        Console.WriteLine($"  Traces   : {result.TracesDirectory}");
        if (traces.Count == 0)
            Console.WriteLine("  No methods matched the selection.");
        return 0;
    }));

    return command;
}

var rootCommand = new RootCommand("LegacyLens — AI-assisted .NET legacy modernization agent.")
{
    CreateAnalyzeCommand(),
    CreateTraceCommand(),
};

return await rootCommand.Parse(args).InvokeAsync();
