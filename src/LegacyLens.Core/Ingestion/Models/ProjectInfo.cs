namespace LegacyLens.Core.Ingestion.Models;

/// <summary>A NuGet package referenced by a project (SDK-style PackageReference or legacy packages.config).</summary>
public sealed record PackageReferenceInfo(string Name, string Version);

/// <summary>Snapshot of a single project loaded from the solution (TDD Layer 1 — Ingestion).</summary>
public sealed record ProjectInfo
{
    public required string Name { get; init; }

    public required string FilePath { get; init; }

    /// <summary>Target framework moniker(s), e.g. "net8.0" or ".NET Framework v4.7.2".</summary>
    public required string TargetFramework { get; init; }

    /// <summary>Names of projects this project references (edges of the dependency DAG).</summary>
    public required IReadOnlyList<string> ProjectReferences { get; init; }

    public required IReadOnlyList<PackageReferenceInfo> PackageReferences { get; init; }

    /// <summary>Number of compilable source documents.</summary>
    public required int DocumentCount { get; init; }

    /// <summary>
    /// True when the project declares packages.config dependencies but the solution's packages
    /// folder is missing. `dotnet restore` does not handle packages.config, so a legacy solution is
    /// easily analyzed un-restored — which silently strips every reference, including mscorlib, and
    /// floods the baseline with meaningless type-resolution errors.
    /// </summary>
    public bool NeedsPackageRestore { get; init; }
}
