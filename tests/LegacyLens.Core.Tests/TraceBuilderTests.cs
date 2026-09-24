using LegacyLens.Core.AppCat;
using LegacyLens.Core.Ingestion;
using LegacyLens.Core.Tracing;
using LegacyLens.Core.Tracing.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace LegacyLens.Core.Tests;

[Collection(SampleSolutionCollection.Name)]
public sealed class TraceBuilderTests(SampleSolutionFixture fixture)
{
    private const string ApplyName = "Alpha.OrderRules.Apply(decimal)";

    [Fact]
    public async Task Build_CapturesContainingTypeSignatureAndResolvedCallees()
    {
        var trace = await TraceAsync(ApplyName);

        Assert.Equal("Alpha", trace.Project);
        Assert.Equal("Alpha/OrderRules.cs", trace.File);
        Assert.Equal(5, trace.Complexity);
        Assert.True(trace.Grounding.IsFullyResolved);

        Assert.Equal(new[] { "Alpha.RulesBase", "object" }, trace.ContainingType.BaseTypes);
        Assert.Contains("Alpha.IRulesEngine", trace.ContainingType.Interfaces);

        Assert.Equal("public", trace.Signature.Accessibility);
        Assert.Equal("decimal", trace.Signature.ReturnType);
        var parameter = Assert.Single(trace.Signature.Parameters);
        Assert.Equal("amount", parameter.Name);
        Assert.Equal("decimal", parameter.Type);

        var audit = Assert.Single(trace.Callees, c => c.Symbol == "Alpha.AuditLog.Record(decimal)");
        Assert.Equal(SymbolOrigin.Source, audit.Origin);
        Assert.Equal("Alpha", audit.Container);

        var max = Assert.Single(trace.Callees, c => c.Symbol == "System.Math.Max(decimal, decimal)");
        Assert.Equal(SymbolOrigin.Metadata, max.Origin);
    }

    [Fact]
    public async Task Build_FindsCallersAcrossProjectsIncludingInterfaceDispatch()
    {
        var trace = await TraceAsync(ApplyName);

        var direct = Assert.Single(trace.Callers, c => c.Symbol == "Beta.BetaClient.Checkout(decimal)");
        Assert.Equal("Beta", direct.Project);
        Assert.Equal("Beta/BetaClient.cs", direct.File);
        Assert.False(direct.ViaOverrideOrInterface);

        var viaInterface = Assert.Single(
            trace.Callers,
            c => c.Symbol == "Beta.BetaClient.CheckoutThrough(Alpha.IRulesEngine, decimal)");
        Assert.True(viaInterface.ViaOverrideOrInterface);
    }

    [Fact]
    public async Task Build_ClassifiesStateAndSummarizesControlAndDataFlow()
    {
        var trace = await TraceAsync(ApplyName);

        Assert.Contains(trace.State, s => s is { Symbol: "Alpha.RulesBase.Threshold", Access: MemberAccessKind.Read });
        Assert.Contains(trace.State, s => s is { Symbol: "Alpha.OrderRules.Tiers", Access: MemberAccessKind.Read });
        Assert.Contains(
            trace.State,
            s => s is { Symbol: "Alpha.OrderRules.LastResult", Kind: "Property", Access: MemberAccessKind.Write });
        Assert.DoesNotContain(trace.State, s => s.Symbol.StartsWith("System.", StringComparison.Ordinal));

        Assert.NotNull(trace.ControlFlow);
        Assert.True(trace.ControlFlow.ConditionalBranches >= 3, "guard, loop condition and tier check");
        Assert.True(trace.ControlFlow.LoopBackEdges >= 1);
        Assert.Equal(1, trace.ControlFlow.ExceptionHandlers);
        Assert.Equal(1, trace.ControlFlow.ThrowPaths);

        Assert.NotNull(trace.DataFlow);
        Assert.Equal(new[] { "amount" }, trace.DataFlow.ParametersRead);
        Assert.Empty(trace.DataFlow.ParametersWritten);
    }

    [Fact]
    public async Task Build_ExcludesCompilerGeneratedQueryMembersFromState()
    {
        var trace = await TraceAsync("Alpha.OrderRules.Discounted()");

        Assert.Contains(trace.State, s => s is { Symbol: "Alpha.OrderRules.Tiers", Access: MemberAccessKind.Read });
        Assert.Contains(trace.State, s => s is { Symbol: "Alpha.OrderRules._discount", Access: MemberAccessKind.Read });
        Assert.DoesNotContain(
            trace.State,
            s => s.Symbol.Contains("anonymous", StringComparison.Ordinal) || s.Symbol.Contains("<>", StringComparison.Ordinal));
        Assert.DoesNotContain(trace.State, s => s.Access != MemberAccessKind.Read);
    }

    [Fact]
    public async Task Build_IncludesDocCommentAndMapsOnlyAppCatIncidentsInsideTheMethod()
    {
        var lines = await File.ReadAllLinesAsync(Path.Combine(fixture.RootPath, "Alpha", "OrderRules.cs"));
        var docLine = Array.FindIndex(lines, l => l.Contains("/// <summary>Applies tiered")) + 1;
        var maxLine = Array.FindIndex(lines, l => l.Contains("Math.Max")) + 1;
        var namespaceLine = Array.FindIndex(lines, l => l.StartsWith("namespace", StringComparison.Ordinal)) + 1;

        var appCat = new AppCatReport("report.json",
        [
            new AppCatIncident("Perf.0001", "Optional", "Synchronous API usage detected", @"Alpha\OrderRules.cs", maxLine, "Alpha", 1),
            new AppCatIncident("Runtime.0003", "Potential", "Old framework", @"Alpha\OrderRules.cs", namespaceLine, "Alpha", 3),
            new AppCatIncident("Local.0003", "Potential", "IO operations", @"Beta\BetaClient.cs", maxLine, "Beta", 3),
        ]);

        var trace = await TraceAsync(ApplyName, appCat);

        var incident = Assert.Single(trace.AppCatIncidents);
        Assert.Equal("Perf.0001", incident.RuleId);
        Assert.Equal(maxLine, incident.Line);

        Assert.Equal(docLine, trace.StartLine);
        var chunk = Assert.Single(trace.Chunks);
        Assert.Equal(ChunkBoundary.Whole, chunk.Boundary);
        Assert.StartsWith("/// <summary>", chunk.Text, StringComparison.Ordinal);
        Assert.EndsWith("}", chunk.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Build_WithSmallBudget_SplitsWithoutLosingSource()
    {
        var whole = await TraceAsync(ApplyName);
        var split = await TraceAsync(ApplyName, tokenBudget: 60);

        Assert.True(split.Chunks.Count > 1);
        Assert.Equal(whole.Chunks[0].Text, string.Join('\n', split.Chunks.Select(c => c.Text)));
        Assert.Equal(whole.StartLine, split.Chunks[0].StartLine);
        Assert.Equal(whole.EndLine, split.Chunks[^1].EndLine);
    }

    [Fact]
    public async Task Select_RanksByComplexityAndHonorsFilters()
    {
        using var analysis = await new ProjectGraphLoader().LoadAsync(fixture.SolutionPath);

        var top = await TraceTargetSelector.SelectAsync(analysis.Solution, analysis.Graph, new TraceSelection { Top = 2 });
        Assert.Equal(
            new[] { ApplyName, "Alpha.AlphaService.SumPositives(int[])" },
            top.Select(t => t.FullyQualifiedName));

        var filtered = await TraceTargetSelector.SelectAsync(
            analysis.Solution,
            analysis.Graph,
            new TraceSelection { MethodFilter = "checkout", ProjectFilter = "beta" });
        Assert.Equal(2, filtered.Count);
        Assert.All(filtered, t => Assert.Contains("Checkout", t.FullyQualifiedName));
    }

    [Fact]
    public async Task Build_FlagsUnresolvedReferencesInsteadOfPresentingThemAsReal()
    {
        using var workspace = new AdhocWorkspace();
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Where(path => Path.GetFileName(path) is "System.Private.CoreLib.dll" or "System.Runtime.dll")
            .Select(path => MetadataReference.CreateFromFile(path));

        var projectId = ProjectId.CreateNewId();
        var solution = workspace.CurrentSolution
            .AddProject(ProjectInfo.Create(
                projectId,
                VersionStamp.Create(),
                "Broken",
                "Broken",
                LanguageNames.CSharp,
                compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary),
                metadataReferences: references))
            .AddDocument(
                DocumentId.CreateNewId(projectId),
                "Broken.cs",
                SourceText.From("public class Broken { public int Run() { return MissingType.Compute(1); } }"));

        var project = solution.GetProject(projectId)!;
        var compilation = (await project.GetCompilationAsync())!;
        var method = compilation.GetTypeByMetadataName("Broken")!.GetMembers("Run").OfType<IMethodSymbol>().Single();

        var trace = await new TraceBuilder(solution, fixture.RootPath).BuildAsync(project, method);

        Assert.False(trace.Grounding.IsFullyResolved);
        Assert.True(trace.Grounding.InvalidOperations > 0);
        Assert.Contains(trace.Grounding.Errors, e => e.StartsWith("CS0103:", StringComparison.Ordinal));

        // A logical document name must not be resolved against the working directory.
        Assert.Equal("Broken.cs", trace.File);
    }

    private async Task<MethodTrace> TraceAsync(
        string fullyQualifiedName,
        AppCatReport? appCat = null,
        int tokenBudget = TraceBuilder.DefaultTokenBudget)
    {
        using var analysis = await new ProjectGraphLoader().LoadAsync(fixture.SolutionPath);
        var target = Assert.Single(
            await TraceTargetSelector.SelectAsync(
                analysis.Solution,
                analysis.Graph,
                new TraceSelection { MethodFilter = fullyQualifiedName }),
            t => t.FullyQualifiedName == fullyQualifiedName);

        var builder = new TraceBuilder(analysis.Solution, fixture.RootPath, appCat, tokenBudget);
        return await builder.BuildAsync(target.Project, target.Method);
    }
}
