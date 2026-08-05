namespace LegacyLens.Core.Ingestion;

/// <summary>
/// Locates .NET Framework reference assemblies (targeting packs) so MSBuildWorkspace can run a
/// compiler-accurate design-time build of legacy non-SDK-style projects.
/// </summary>
/// <remarks>
/// Without them MSBuild reports "The reference assemblies for .NETFramework,Version=vX.Y were not
/// found", falls back to the runtime directory, and resolves nothing properly — every BCL type
/// becomes an error symbol and the semantic model is useless for trace generation. Machines with
/// Visual Studio may have the packs under Program Files, but that location is frequently populated
/// with XML documentation only, so each candidate is validated before use. Everywhere else (CI
/// runners, Linux containers, boxes with just the .NET SDK) they come from the
/// Microsoft.NETFramework.ReferenceAssemblies.* NuGet packages this project references.
/// </remarks>
public static class ReferenceAssemblyResolver
{
    private const string FrameworkFolder = ".NETFramework";

    private static readonly Lazy<string?> Resolved = new(Resolve, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>
    /// Path to pass as the MSBuild <c>TargetFrameworkRootPath</c> property — a directory containing
    /// a <c>.NETFramework</c> folder with <c>vX.Y</c> subdirectories. Null when no usable reference
    /// assemblies exist, in which case legacy projects load with degraded semantics.
    /// </summary>
    public static string? TargetFrameworkRootPath => Resolved.Value;

    private static string? Resolve()
    {
        var installedRoot = InstalledReferenceAssemblyRoot();

        var usable = EnumerateCandidateVersionDirectories(installedRoot)
            .Where(IsUsable)
            .ToList();
        if (usable.Count == 0)
            return null;

        // Fast path: a complete machine-wide install already exposes every version under one root.
        if (installedRoot is not null
            && usable.All(d => d.StartsWith(installedRoot, StringComparison.OrdinalIgnoreCase)))
        {
            return installedRoot;
        }

        return StageMergedRoot(usable);
    }

    /// <summary>
    /// MSBuild's GetReferenceAssemblyPaths needs RedistList/FrameworkList.xml; a directory holding
    /// only the XML documentation (a common Visual Studio side effect) silently resolves nothing.
    /// </summary>
    private static bool IsUsable(string versionDirectory) =>
        File.Exists(Path.Combine(versionDirectory, "RedistList", "FrameworkList.xml"));

    private static string? InstalledReferenceAssemblyRoot()
    {
        foreach (var variable in new[] { "ProgramFiles(x86)", "ProgramFiles" })
        {
            var programFiles = Environment.GetEnvironmentVariable(variable);
            if (string.IsNullOrEmpty(programFiles))
                continue;

            var root = Path.Combine(programFiles, "Reference Assemblies", "Microsoft", "Framework");
            if (Directory.Exists(Path.Combine(root, FrameworkFolder)))
                return root;
        }

        return null;
    }

    private static IEnumerable<string> EnumerateCandidateVersionDirectories(string? installedRoot)
    {
        // Machine-wide install first so it wins the per-version dedupe in StageMergedRoot.
        if (installedRoot is not null)
        {
            foreach (var directory in Directory.EnumerateDirectories(Path.Combine(installedRoot, FrameworkFolder), "v*"))
                yield return directory;
        }

        foreach (var directory in PackagedVersionDirectories())
            yield return directory;
    }

    /// <summary>Enumerates every <c>build/.NETFramework/vX.Y</c> directory in the NuGet package cache.</summary>
    private static IEnumerable<string> PackagedVersionDirectories()
    {
        var packagesRoot = Environment.GetEnvironmentVariable("NUGET_PACKAGES");
        if (string.IsNullOrEmpty(packagesRoot))
        {
            packagesRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".nuget",
                "packages");
        }

        if (!Directory.Exists(packagesRoot))
            yield break;

        foreach (var package in Directory.EnumerateDirectories(packagesRoot, "microsoft.netframework.referenceassemblies.net*"))
        {
            foreach (var version in Directory.EnumerateDirectories(package))
            {
                var frameworkDirectory = Path.Combine(version, "build", FrameworkFolder);
                if (!Directory.Exists(frameworkDirectory))
                    continue;

                foreach (var tfm in Directory.EnumerateDirectories(frameworkDirectory, "v*"))
                    yield return tfm;
            }
        }
    }

    /// <summary>
    /// MSBuild accepts a single <c>TargetFrameworkRootPath</c>, but each TFM ships in its own NuGet
    /// package, so the individual version directories are staged under one cached root.
    /// </summary>
    private static string StageMergedRoot(IEnumerable<string> versionDirectories)
    {
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LegacyLens",
            "ReferenceAssemblies");
        var frameworkDirectory = Path.Combine(root, FrameworkFolder);
        Directory.CreateDirectory(frameworkDirectory);

        var staged = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var source in versionDirectories)
        {
            var version = Path.GetFileName(source);
            if (!staged.Add(version))
                continue;

            var destination = Path.Combine(frameworkDirectory, version);
            if (IsUsable(destination))
                continue;

            LinkOrCopy(source, destination);
        }

        return root;
    }

    private static void LinkOrCopy(string source, string destination)
    {
        try
        {
            // Cheapest option, but Windows only grants it under Developer Mode or elevation.
            Directory.CreateSymbolicLink(destination, source);
            return;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            // Fall through to copying.
        }

        CopyDirectory(source, destination);
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);

        foreach (var file in Directory.EnumerateFiles(source))
        {
            // Only assemblies and the redist manifest matter; skipping the XML docs beside them
            // roughly halves what gets staged.
            var extension = Path.GetExtension(file);
            if (extension.Equals(".xml", StringComparison.OrdinalIgnoreCase)
                && !Path.GetFileName(file).Equals("FrameworkList.xml", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), overwrite: true);
        }

        foreach (var directory in Directory.EnumerateDirectories(source))
            CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)));
    }
}
