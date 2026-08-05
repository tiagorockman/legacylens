using System.Xml.Linq;
using LegacyLens.Core.Ingestion.Models;
using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;
using ProjectInfo = LegacyLens.Core.Ingestion.Models.ProjectInfo;

namespace LegacyLens.Core.Ingestion;

/// <summary>
/// Result of loading a solution: the Roslyn <see cref="Solution"/> plus the project
/// dependency graph. Owns the underlying <see cref="MSBuildWorkspace"/>.
/// </summary>
public sealed class SolutionAnalysis : IDisposable
{
    public required string SolutionPath { get; init; }

    public required Solution Solution { get; init; }

    public required ProjectGraph Graph { get; init; }

    /// <summary>Non-fatal workspace load failures (missing SDKs, unresolved references, ...).</summary>
    public required IReadOnlyList<string> WorkspaceDiagnostics { get; init; }

    internal MSBuildWorkspace? Workspace { get; init; }

    public void Dispose() => Workspace?.Dispose();
}

/// <summary>
/// TDD Layer 1 — Ingestion. Loads a .sln via MSBuildWorkspace, enumerates compilable
/// projects, and emits the project reference DAG.
/// </summary>
public sealed class ProjectGraphLoader
{
    private static readonly object RegistrationLock = new();

    /// <summary>
    /// Registers the MSBuild instance from the installed .NET SDK. Must run before any
    /// Microsoft.Build type is loaded; safe to call multiple times.
    /// </summary>
    public static void EnsureMsBuildRegistered()
    {
        lock (RegistrationLock)
        {
            if (!MSBuildLocator.IsRegistered)
                MSBuildLocator.RegisterDefaults();
        }
    }

    public async Task<SolutionAnalysis> LoadAsync(
        string solutionPath,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var fullPath = Path.GetFullPath(solutionPath);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException($"Solution file not found: {fullPath}", fullPath);

        EnsureMsBuildRegistered();

        var diagnostics = new List<string>();

        // Legacy non-SDK projects need the .NET Framework targeting packs to resolve even mscorlib;
        // without them the semantic model degrades to error symbols (see ReferenceAssemblyResolver).
        var properties = new Dictionary<string, string>();
        var referenceAssemblyRoot = ReferenceAssemblyResolver.TargetFrameworkRootPath;
        if (referenceAssemblyRoot is not null)
        {
            properties["TargetFrameworkRootPath"] = referenceAssemblyRoot;
            progress?.Report($"Using .NET Framework reference assemblies from {referenceAssemblyRoot}");
        }

        var workspace = MSBuildWorkspace.Create(properties);
        workspace.RegisterWorkspaceFailedHandler(e =>
        {
            lock (diagnostics)
            {
                diagnostics.Add($"[{e.Diagnostic.Kind}] {e.Diagnostic.Message}");
            }
        });

        progress?.Report($"Loading solution {Path.GetFileName(fullPath)} via MSBuildWorkspace...");
        var solution = await workspace.OpenSolutionAsync(fullPath, cancellationToken: cancellationToken);

        // Multi-targeted projects surface once per TFM (e.g. "Lib (net8.0)"); collapse to one
        // entry per project file so the graph has a single node per project.
        var projects = new List<ProjectInfo>();
        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var project in solution.Projects.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase))
        {
            if (project.FilePath is not null && !seenPaths.Add(project.FilePath))
                continue;

            progress?.Report($"  Inspecting project {project.Name}");
            projects.Add(CreateProjectInfo(project, solution, Path.GetDirectoryName(fullPath)!));
        }

        return new SolutionAnalysis
        {
            SolutionPath = fullPath,
            Solution = solution,
            Graph = ProjectGraph.Build(projects),
            WorkspaceDiagnostics = diagnostics,
            Workspace = workspace,
        };
    }

    private static ProjectInfo CreateProjectInfo(Project project, Solution solution, string solutionDirectory)
    {
        var referencedNames = project.ProjectReferences
            .Select(r => solution.GetProject(r.ProjectId)?.Name)
            .Where(n => n is not null)
            .Select(NormalizeProjectName!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new ProjectInfo
        {
            Name = NormalizeProjectName(project.Name),
            FilePath = project.FilePath ?? "(in-memory)",
            TargetFramework = ResolveTargetFramework(project.FilePath),
            ProjectReferences = referencedNames,
            PackageReferences = ResolvePackageReferences(project.FilePath),
            DocumentCount = project.Documents.Count(),
            NeedsPackageRestore = NeedsPackageRestore(project.FilePath, solutionDirectory),
        };
    }

    /// <summary>Strips the TFM suffix Roslyn appends to multi-targeted projects ("Lib (net8.0)" → "Lib").</summary>
    private static string NormalizeProjectName(string name)
    {
        var index = name.IndexOf(" (", StringComparison.Ordinal);
        return index > 0 && name.EndsWith(')') ? name[..index] : name;
    }

    /// <summary>
    /// A packages.config project whose solution has no packages folder was never restored — MSBuild
    /// then fails to import the packages' targets and resolves no references at all.
    /// </summary>
    internal static bool NeedsPackageRestore(string? projectFilePath, string solutionDirectory)
    {
        if (projectFilePath is null)
            return false;

        var packagesConfig = Path.Combine(Path.GetDirectoryName(projectFilePath)!, "packages.config");
        if (!File.Exists(packagesConfig))
            return false;

        return !Directory.Exists(Path.Combine(solutionDirectory, "packages"));
    }

    internal static string ResolveTargetFramework(string? projectFilePath)
    {
        if (projectFilePath is null || !File.Exists(projectFilePath))
            return "unknown";

        try
        {
            var document = XDocument.Load(projectFilePath);
            var elements = document.Descendants().ToList();

            var sdkStyle = elements.FirstOrDefault(e => e.Name.LocalName == "TargetFramework")?.Value
                ?? elements.FirstOrDefault(e => e.Name.LocalName == "TargetFrameworks")?.Value;
            if (!string.IsNullOrWhiteSpace(sdkStyle))
                return sdkStyle;

            var legacy = elements.FirstOrDefault(e => e.Name.LocalName == "TargetFrameworkVersion")?.Value;
            return string.IsNullOrWhiteSpace(legacy) ? "unknown" : $".NET Framework {legacy}";
        }
        catch (Exception ex) when (ex is System.Xml.XmlException or IOException)
        {
            return "unknown";
        }
    }

    internal static IReadOnlyList<PackageReferenceInfo> ResolvePackageReferences(string? projectFilePath)
    {
        if (projectFilePath is null || !File.Exists(projectFilePath))
            return [];

        var packages = new List<PackageReferenceInfo>();
        try
        {
            var document = XDocument.Load(projectFilePath);
            foreach (var element in document.Descendants().Where(e => e.Name.LocalName == "PackageReference"))
            {
                var name = element.Attribute("Include")?.Value;
                if (string.IsNullOrWhiteSpace(name))
                    continue;

                var version = element.Attribute("Version")?.Value
                    ?? element.Elements().FirstOrDefault(e => e.Name.LocalName == "Version")?.Value
                    ?? "*";
                packages.Add(new PackageReferenceInfo(name, version));
            }

            // Legacy .NET Framework projects keep packages in packages.config next to the csproj.
            var packagesConfig = Path.Combine(Path.GetDirectoryName(projectFilePath)!, "packages.config");
            if (File.Exists(packagesConfig))
            {
                foreach (var element in XDocument.Load(packagesConfig).Descendants()
                             .Where(e => e.Name.LocalName == "package"))
                {
                    var id = element.Attribute("id")?.Value;
                    if (!string.IsNullOrWhiteSpace(id))
                        packages.Add(new PackageReferenceInfo(id, element.Attribute("version")?.Value ?? "*"));
                }
            }
        }
        catch (Exception ex) when (ex is System.Xml.XmlException or IOException)
        {
            // Unparseable project file: report the project with no package data rather than failing the run.
        }

        return packages
            .DistinctBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
