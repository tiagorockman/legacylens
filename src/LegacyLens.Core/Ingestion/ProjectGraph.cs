using LegacyLens.Core.Ingestion.Models;

namespace LegacyLens.Core.Ingestion;

/// <summary>
/// Directed graph of project references across the solution, with circular-dependency
/// detection (TDD §4.1 Layer 1 / §5 migration-wave ordering groundwork).
/// </summary>
public sealed class ProjectGraph
{
    private ProjectGraph(
        IReadOnlyList<ProjectInfo> projects,
        IReadOnlyList<(string From, string To)> edges,
        IReadOnlyList<IReadOnlyList<string>> cycles)
    {
        Projects = projects;
        Edges = edges;
        Cycles = cycles;
    }

    public IReadOnlyList<ProjectInfo> Projects { get; }

    /// <summary>Edges (From references To), restricted to projects that exist in the solution.</summary>
    public IReadOnlyList<(string From, string To)> Edges { get; }

    /// <summary>Detected reference cycles; empty for a healthy DAG.</summary>
    public IReadOnlyList<IReadOnlyList<string>> Cycles { get; }

    public static ProjectGraph Build(IReadOnlyList<ProjectInfo> projects)
    {
        var known = projects.Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var edges = projects
            .SelectMany(p => p.ProjectReferences
                .Where(r => known.Contains(r))
                .Select(r => (From: p.Name, To: r)))
            .Distinct()
            .ToList();

        var adjacency = edges
            .GroupBy(e => e.From, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Select(e => e.To).ToList(), StringComparer.OrdinalIgnoreCase);

        return new ProjectGraph(projects, edges, DetectCycles(projects, adjacency));
    }

    private static List<IReadOnlyList<string>> DetectCycles(
        IReadOnlyList<ProjectInfo> projects,
        IReadOnlyDictionary<string, List<string>> adjacency)
    {
        var cycles = new List<IReadOnlyList<string>>();
        var state = new Dictionary<string, VisitState>(StringComparer.OrdinalIgnoreCase);
        var stack = new List<string>();

        foreach (var project in projects)
        {
            if (state.GetValueOrDefault(project.Name) == VisitState.Unvisited)
                Visit(project.Name);
        }

        return cycles;

        void Visit(string node)
        {
            state[node] = VisitState.InProgress;
            stack.Add(node);

            foreach (var next in adjacency.GetValueOrDefault(node) ?? [])
            {
                var nextState = state.GetValueOrDefault(next);
                if (nextState == VisitState.Unvisited)
                {
                    Visit(next);
                }
                else if (nextState == VisitState.InProgress)
                {
                    var start = stack.FindIndex(n => string.Equals(n, next, StringComparison.OrdinalIgnoreCase));
                    cycles.Add([.. stack.Skip(start), next]);
                }
            }

            stack.RemoveAt(stack.Count - 1);
            state[node] = VisitState.Done;
        }
    }

    private enum VisitState
    {
        Unvisited = 0,
        InProgress,
        Done,
    }
}
