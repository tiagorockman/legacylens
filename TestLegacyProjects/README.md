# Project Execution Test

Legacy test subjects analyzed by LegacyLens. The cloned sources are git-ignored
(they are separate repositories) — only the generated baseline reports are versioned here.

Run everything from the repository root.

## ContosoUniversityDotNetCore

Reference solution cited in the LegacyLens TDD (`netcoreapp2.0`, out of support since 2018).

```bash
# 1. Clone the test subject (git-ignored)
git clone https://github.com/jbogard/ContosoUniversityDotNetCore.git TestLegacyProjects/ContosoUniversityDotNetCore

# 2. Restore it so MSBuildWorkspace can run the design-time build
dotnet restore .\TestLegacyProjects\ContosoUniversityDotNetCore\ContosoUniversity.sln

# 3. Run the LegacyLens baseline analysis
dotnet run --project src/LegacyLens.CLI -- analyze --solution .\TestLegacyProjects\ContosoUniversityDotNetCore\ContosoUniversity.sln --output TestLegacyProjects\ContosoUniversityDotNetCore-out
```

Output: [ContosoUniversityDotNetCore-out/solution-baseline.md](ContosoUniversityDotNetCore-out/solution-baseline.md)
