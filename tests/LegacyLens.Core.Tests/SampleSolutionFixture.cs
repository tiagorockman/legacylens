using System.Diagnostics;

namespace LegacyLens.Core.Tests;

/// <summary>
/// Generates a two-project SDK-style solution (Beta → Alpha) in a temp directory and
/// restores it once, so MSBuildWorkspace-based tests can run a real design-time build.
/// </summary>
public sealed class SampleSolutionFixture : IDisposable
{
    public string RootPath { get; }

    public string SolutionPath { get; }

    public SampleSolutionFixture()
    {
        RootPath = Path.Combine(Path.GetTempPath(), "legacylens-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(RootPath, "Alpha"));
        Directory.CreateDirectory(Path.Combine(RootPath, "Beta"));
        SolutionPath = Path.Combine(RootPath, "Sample.sln");

        File.WriteAllText(Path.Combine(RootPath, "Alpha", "Alpha.csproj"),
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net8.0</TargetFramework>
                <Nullable>enable</Nullable>
              </PropertyGroup>
            </Project>
            """);

        File.WriteAllText(Path.Combine(RootPath, "Alpha", "AlphaService.cs"),
            """
            namespace Alpha;

            public class AlphaService
            {
                public int SumPositives(int[] values)
                {
                    var total = 0;
                    foreach (var value in values)
                    {
                        if (value > 0)
                            total += value;
                    }

                    return total;
                }

                [System.Obsolete("Use SumPositives instead.")]
                public int LegacySum(int[] values) => values.Length;
            }
            """);

        // Exercises what traces must capture: inheritance, an interface, member state, a guard
        // throw, a loop, exception handling and a doc comment above the method.
        File.WriteAllText(Path.Combine(RootPath, "Alpha", "OrderRules.cs"),
            """
            using System.Linq;

            namespace Alpha;

            public interface IRulesEngine
            {
                decimal Apply(decimal amount);
            }

            public abstract class RulesBase
            {
                protected decimal Threshold = 10m;
            }

            public sealed class AuditLog
            {
                public void Record(decimal amount)
                {
                }
            }

            public class OrderRules : RulesBase, IRulesEngine
            {
                private static readonly decimal[] Tiers = { 100m, 500m, 1000m };
                private readonly decimal _discount = 5m;
                private readonly AuditLog _audit = new();

                public decimal LastResult { get; private set; }

                /// <summary>Applies tiered discounts, never going below the threshold.</summary>
                public decimal Apply(decimal amount)
                {
                    if (amount < 0)
                        throw new System.ArgumentOutOfRangeException(nameof(amount));

                    var total = amount;
                    for (var i = 0; i < Tiers.Length; i++)
                    {
                        if (total > Tiers[i])
                            total -= _discount;
                    }

                    try
                    {
                        _audit.Record(total);
                    }
                    catch (System.InvalidOperationException)
                    {
                        return amount;
                    }

                    LastResult = total;
                    return System.Math.Max(total, Threshold);
                }

                // Query syntax with an anonymous projection, as legacy data-access code is full of.
                public decimal[] Discounted() =>
                    (from tier in Tiers
                     select new { tier, discounted = tier - _discount })
                    .Select(x => x.discounted)
                    .ToArray();
            }
            """);

        File.WriteAllText(Path.Combine(RootPath, "Beta", "Beta.csproj"),
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net8.0</TargetFramework>
                <Nullable>enable</Nullable>
              </PropertyGroup>
              <ItemGroup>
                <ProjectReference Include="..\Alpha\Alpha.csproj" />
              </ItemGroup>
            </Project>
            """);

        File.WriteAllText(Path.Combine(RootPath, "Beta", "BetaClient.cs"),
            """
            namespace Beta;

            public class BetaClient
            {
                private readonly Alpha.AlphaService _service = new();

                public int Run(int[] values) => _service.SumPositives(values);

                public decimal Checkout(decimal amount) => new Alpha.OrderRules().Apply(amount);

                public decimal CheckoutThrough(Alpha.IRulesEngine engine, decimal amount) => engine.Apply(amount);
            }
            """);

        const string alphaGuid = "{11111111-1111-1111-1111-111111111111}";
        const string betaGuid = "{22222222-2222-2222-2222-222222222222}";
        File.WriteAllText(SolutionPath,
            $$"""
            Microsoft Visual Studio Solution File, Format Version 12.00
            # Visual Studio Version 17
            Project("{9A19103F-16F7-4668-BE54-9A1E7A4F7556}") = "Alpha", "Alpha\Alpha.csproj", "{{alphaGuid}}"
            EndProject
            Project("{9A19103F-16F7-4668-BE54-9A1E7A4F7556}") = "Beta", "Beta\Beta.csproj", "{{betaGuid}}"
            EndProject
            Global
            	GlobalSection(SolutionConfigurationPlatforms) = preSolution
            		Debug|Any CPU = Debug|Any CPU
            	EndGlobalSection
            	GlobalSection(ProjectConfigurationPlatforms) = postSolution
            		{{alphaGuid}}.Debug|Any CPU.ActiveCfg = Debug|Any CPU
            		{{alphaGuid}}.Debug|Any CPU.Build.0 = Debug|Any CPU
            		{{betaGuid}}.Debug|Any CPU.ActiveCfg = Debug|Any CPU
            		{{betaGuid}}.Debug|Any CPU.Build.0 = Debug|Any CPU
            	EndGlobalSection
            EndGlobal
            """);

        RunDotnet($"restore \"{SolutionPath}\"");
    }

    private void RunDotnet(string arguments)
    {
        var startInfo = new ProcessStartInfo("dotnet", arguments)
        {
            WorkingDirectory = RootPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start dotnet.");

        // Both pipes must be drained concurrently: reading one to the end while the child fills the
        // other deadlocks once the unread pipe's buffer is full.
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        var stdout = stdoutTask.GetAwaiter().GetResult();
        var stderr = stderrTask.GetAwaiter().GetResult();
        process.WaitForExit();

        if (process.ExitCode != 0)
            throw new InvalidOperationException($"dotnet {arguments} failed:\n{stdout}\n{stderr}");
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(RootPath, recursive: true);
        }
        catch (IOException)
        {
            // Temp cleanup is best-effort; the OS temp sweeper handles leftovers.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}

[CollectionDefinition(Name)]
public sealed class SampleSolutionCollection : ICollectionFixture<SampleSolutionFixture>
{
    public const string Name = "SampleSolution";
}
