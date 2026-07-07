using LegacyLens.Core.Ingestion;
using LegacyLens.Core.Ingestion.Models;

namespace LegacyLens.Core.Tests;

public sealed class ProjectGraphTests
{
    [Fact]
    public void Build_DetectsCycle()
    {
        var graph = ProjectGraph.Build(
        [
            Project("A", "B"),
            Project("B", "C"),
            Project("C", "A"),
        ]);

        var cycle = Assert.Single(graph.Cycles);
        Assert.Equal(4, cycle.Count);
        Assert.Equal(cycle[0], cycle[^1]);
        Assert.Equal(new[] { "A", "B", "C" }, cycle.Take(3).OrderBy(n => n));
    }

    [Fact]
    public void Build_HealthyDagHasNoCycles()
    {
        var graph = ProjectGraph.Build(
        [
            Project("App", "Domain", "Infra"),
            Project("Infra", "Domain"),
            Project("Domain"),
        ]);

        Assert.Empty(graph.Cycles);
        Assert.Equal(3, graph.Edges.Count);
    }

    [Fact]
    public void Build_IgnoresReferencesToProjectsOutsideTheSolution()
    {
        var graph = ProjectGraph.Build([Project("App", "NotInSolution")]);

        Assert.Empty(graph.Edges);
        Assert.Empty(graph.Cycles);
    }

    private static ProjectInfo Project(string name, params string[] references) => new()
    {
        Name = name,
        FilePath = $"{name}.csproj",
        TargetFramework = "net8.0",
        ProjectReferences = references,
        PackageReferences = [],
        DocumentCount = 1,
    };
}
