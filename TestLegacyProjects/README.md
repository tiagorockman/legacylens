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
dotnet run --project src/LegacyLens.CLI -- trace --solution .\TestLegacyProjects\nopCommerce\src\NopCommerce.sln --appcat TestLegacyProjects\nopCommerce-appcat.json --output TestLegacyProjects\nopCommerce-out
```

### SmartStoreNET 4.2.0 — legacy .NET Framework, different TFM

```bash
git clone --branch 4.2.0 https://github.com/smartstore/SmartStoreNET.git TestLegacyProjects/SmartStoreNET
nuget restore .\TestLegacyProjects\SmartStoreNET\src\SmartStoreNET.sln
dotnet run --project src/LegacyLens.CLI -- analyze --solution .\TestLegacyProjects\SmartStoreNET\src\SmartStoreNET.sln --output TestLegacyProjects\SmartStoreNET-out
dotnet run --project src/LegacyLens.CLI -- trace --solution .\TestLegacyProjects\SmartStoreNET\src\SmartStoreNET.sln --output TestLegacyProjects\SmartStoreNET-out
```

> `packages.config` solutions must be restored with `nuget restore`, not `dotnet restore`, which does
> not support that format. LegacyLens detects the un-restored case and prints the fix at the top of
> the report instead of emitting thousands of unresolved-type errors.

## Organization constraints

[example-constraints-policy.json](example-constraints-policy.json) is an illustrative policy — it does
not represent any real organization — used to exercise constraint evaluation against these solutions.
It covers approved target frameworks, prohibited packages, minimum dependency versions, a rule scoped
to projects that do not exist here, a requirement that only a person can verify, and a rule kind this
version does not implement, so all four verdicts are exercised on real data:

```bash
dotnet run --project src/LegacyLens.CLI -- analyze --solution .\TestLegacyProjects\nopCommerce\src\NopCommerce.sln --appcat TestLegacyProjects\nopCommerce-appcat.json --constraints TestLegacyProjects\example-constraints-policy.json --output TestLegacyProjects\nopCommerce-out
```

## Traces

`trace` output for the two legacy solutions is summarized in
[nopCommerce-out/trace-index.md](nopCommerce-out/trace-index.md) and
[SmartStoreNET-out/trace-index.md](SmartStoreNET-out/trace-index.md): all 40 traced methods are fully
resolved by the compiler.

> Only the indexes are versioned. The individual traces under `traces/` embed the full source of each
> method, which belongs to the test subjects and is covered by their own licenses — regenerate them
> locally with the commands above.
