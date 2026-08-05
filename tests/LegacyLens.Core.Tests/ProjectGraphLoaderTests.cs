using LegacyLens.Core.Ingestion;

namespace LegacyLens.Core.Tests;

[Collection(SampleSolutionCollection.Name)]
public sealed class ProjectGraphLoaderTests(SampleSolutionFixture fixture)
{
    [Fact]
    public async Task LoadAsync_BuildsProjectGraphWithDependencyEdge()
    {
        var loader = new ProjectGraphLoader();
        using var analysis = await loader.LoadAsync(fixture.SolutionPath);

        Assert.Equal(2, analysis.Graph.Projects.Count);

        var alpha = Assert.Single(analysis.Graph.Projects, p => p.Name == "Alpha");
        var beta = Assert.Single(analysis.Graph.Projects, p => p.Name == "Beta");

        Assert.Equal("net8.0", alpha.TargetFramework);
        Assert.Equal("net8.0", beta.TargetFramework);
        Assert.True(alpha.DocumentCount > 0);

        Assert.Empty(alpha.ProjectReferences);
        Assert.Equal(["Alpha"], beta.ProjectReferences);
        Assert.Contains(("Beta", "Alpha"), analysis.Graph.Edges);
        Assert.Empty(analysis.Graph.Cycles);
    }

    [Fact]
    public async Task LoadAsync_MissingSolution_Throws()
    {
        var loader = new ProjectGraphLoader();
        await Assert.ThrowsAsync<FileNotFoundException>(
            () => loader.LoadAsync(Path.Combine(fixture.RootPath, "does-not-exist.sln")));
    }

    [Fact]
    public void ResolveTargetFramework_ReadsLegacyFrameworkVersion()
    {
        var path = Path.Combine(fixture.RootPath, "legacy.csproj");
        File.WriteAllText(path,
            """
            <?xml version="1.0" encoding="utf-8"?>
            <Project ToolsVersion="15.0" xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
              <PropertyGroup>
                <TargetFrameworkVersion>v4.7.2</TargetFrameworkVersion>
              </PropertyGroup>
            </Project>
            """);

        Assert.Equal(".NET Framework v4.7.2", ProjectGraphLoader.ResolveTargetFramework(path));
    }

    [Fact]
    public void NeedsPackageRestore_TrueOnlyForUnrestoredPackagesConfigProjects()
    {
        var solutionDirectory = Path.Combine(fixture.RootPath, "restore-check");
        var projectDirectory = Path.Combine(solutionDirectory, "Legacy");
        Directory.CreateDirectory(projectDirectory);
        var csproj = Path.Combine(projectDirectory, "Legacy.csproj");
        File.WriteAllText(csproj, "<Project />");

        // No packages.config at all: nothing to restore.
        Assert.False(ProjectGraphLoader.NeedsPackageRestore(csproj, solutionDirectory));

        File.WriteAllText(Path.Combine(projectDirectory, "packages.config"),
            """
            <?xml version="1.0" encoding="utf-8"?>
            <packages>
              <package id="Newtonsoft.Json" version="12.0.3" targetFramework="net472" />
            </packages>
            """);
        Assert.True(ProjectGraphLoader.NeedsPackageRestore(csproj, solutionDirectory));

        Directory.CreateDirectory(Path.Combine(solutionDirectory, "packages"));
        Assert.False(ProjectGraphLoader.NeedsPackageRestore(csproj, solutionDirectory));
    }

    [Fact]
    public void ResolvePackageReferences_ReadsSdkStyleAndPackagesConfig()
    {
        var directory = Path.Combine(fixture.RootPath, "pkgtest");
        Directory.CreateDirectory(directory);
        var csproj = Path.Combine(directory, "pkg.csproj");
        File.WriteAllText(csproj,
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <PackageReference Include="Newtonsoft.Json" Version="13.0.3" />
              </ItemGroup>
            </Project>
            """);
        File.WriteAllText(Path.Combine(directory, "packages.config"),
            """
            <?xml version="1.0" encoding="utf-8"?>
            <packages>
              <package id="EntityFramework" version="6.4.4" targetFramework="net472" />
            </packages>
            """);

        var packages = ProjectGraphLoader.ResolvePackageReferences(csproj);

        Assert.Equal(2, packages.Count);
        Assert.Contains(packages, p => p is { Name: "Newtonsoft.Json", Version: "13.0.3" });
        Assert.Contains(packages, p => p is { Name: "EntityFramework", Version: "6.4.4" });
    }
}
