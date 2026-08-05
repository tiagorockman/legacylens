# Phase 1 Validation — Reference Solutions

Legacy solutions LegacyLens is validated against. The cloned sources are git-ignored (they are
separate repositories); only the generated baseline reports are versioned here.

The Phase 1 milestone requires a baseline in under 5 minutes on 3 OSS solutions. All three complete
in well under a minute on a machine with **no Visual Studio installed** — only the .NET 10 SDK.

| Solution | Target Framework | Projects | Types / Methods | Baseline | Report |
|---|---|---:|---:|---:|---|
| [ContosoUniversityDotNetCore](https://github.com/jbogard/ContosoUniversityDotNetCore) | `netcoreapp2.0` | 2 | 163 / 205 | **2.5s** | [report](ContosoUniversityDotNetCore-out/solution-baseline.md) |
| [nopCommerce 3.90](https://github.com/nopSolutions/nopCommerce) | `.NET Framework 4.5.1` | 31 | 2,408 / 7,207 | **15.1s** | [report](nopCommerce-out/solution-baseline.md) |
| [SmartStoreNET 4.2.0](https://github.com/smartstore/SmartStoreNET) | `.NET Framework 4.7.2` | 25 | 4,142 / 13,968 | **18.7s** | [report](SmartStoreNET-out/solution-baseline.md) |

Every project in all three solutions compiles cleanly under Roslyn, so the semantic model behind the
baselines is compiler-accurate rather than a syntax-only approximation. The nopCommerce run also
ingests a real AppCAT report (1,201 incidents, 2,453 story points).

> `Directory.Build.props` in this folder is intentionally empty and does not import the parent. It
> stops the analyzed solutions from inheriting the LegacyLens repository's own build settings, which
> otherwise injected thousands of phantom nullable-reference errors into their baselines.

## Reproducing

Run everything from the repository root.

### ContosoUniversityDotNetCore — modern SDK-style

```bash
git clone https://github.com/jbogard/ContosoUniversityDotNetCore.git TestLegacyProjects/ContosoUniversityDotNetCore
dotnet restore .\TestLegacyProjects\ContosoUniversityDotNetCore\ContosoUniversity.sln
dotnet run --project src/LegacyLens.CLI -- analyze --solution .\TestLegacyProjects\ContosoUniversityDotNetCore\ContosoUniversity.sln --output TestLegacyProjects\ContosoUniversityDotNetCore-out
```

### nopCommerce 3.90 — legacy .NET Framework, with AppCAT

```bash
git clone --branch release-3.90 https://github.com/nopSolutions/nopCommerce.git TestLegacyProjects/nopCommerce
nuget restore .\TestLegacyProjects\nopCommerce\src\NopCommerce.sln

dotnet tool install -g dotnet-appcat
appcat analyze .\TestLegacyProjects\nopCommerce\src\NopCommerce.sln --source Solution --target AppService.Windows --code --serializer json --report TestLegacyProjects\nopCommerce-appcat --non-interactive --privacyMode Protected

dotnet run --project src/LegacyLens.CLI -- analyze --solution .\TestLegacyProjects\nopCommerce\src\NopCommerce.sln --appcat TestLegacyProjects\nopCommerce-appcat.json --output TestLegacyProjects\nopCommerce-out
```

### SmartStoreNET 4.2.0 — legacy .NET Framework, different TFM

```bash
git clone --branch 4.2.0 https://github.com/smartstore/SmartStoreNET.git TestLegacyProjects/SmartStoreNET
nuget restore .\TestLegacyProjects\SmartStoreNET\src\SmartStoreNET.sln
dotnet run --project src/LegacyLens.CLI -- analyze --solution .\TestLegacyProjects\SmartStoreNET\src\SmartStoreNET.sln --output TestLegacyProjects\SmartStoreNET-out
```

> `packages.config` solutions must be restored with `nuget restore`, not `dotnet restore`, which does
> not support that format. LegacyLens detects the un-restored case and prints the fix at the top of
> the report instead of emitting thousands of unresolved-type errors.
