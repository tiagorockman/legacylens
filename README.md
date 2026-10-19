# LegacyLens 🔍

> **AI-Assisted .NET/C# Legacy Modernization Agent**

![Status](https://img.shields.io/badge/status-Phase%202%20In%20Progress-yellow)
![License](https://img.shields.io/badge/license-MIT-blue)
![Platform](https://img.shields.io/badge/platform-.NET%2010-purple)
![LLM](https://img.shields.io/badge/LLM-Anthropic%20Claude-orange)
![Build](https://img.shields.io/badge/build-passing-brightgreen)

LegacyLens is an open-source, AI-assisted modernization agent built in **C#/.NET 10** that helps organizations analyze, refactor, and migrate legacy .NET Framework codebases into secure, cloud-ready architectures aligned with modern compliance requirements (HIPAA, PCI DSS, FedRAMP, CMMC).

Unlike existing tools that feed raw source files to an LLM, LegacyLens grounds every AI prompt in **compiler-accurate semantic information** extracted via the Roslyn compiler API (`Microsoft.CodeAnalysis`) — eliminating hallucinated package references and invalid type mismatches documented as failure modes in commercial alternatives.

---

## The Problem

The U.S. federal government spends approximately **80% of its $100+ billion annual IT budget** on maintaining legacy systems (GAO-25-107795). The healthcare sector sustains an average data breach cost of **$9.77 million per incident** (IBM 2024), with legacy system vulnerabilities as a primary contributing factor.

Existing commercial modernization tools share three critical limitations:

| Tool | Limitation |
|------|-----------|
| GitHub Copilot App Modernization | Closed-source; paid subscription; documented hallucinated NuGet packages |
| Amazon Q Developer (.NET) | Single-vendor lock-in; SaaS-only; not on-premises deployable |
| Azure Migrate AppCAT | Static analysis only; no LLM reasoning; no automated refactoring |

LegacyLens is built to fill this gap: **open-source, Roslyn-grounded, provider-neutral, and on-premises deployable** for regulated enterprises that cannot route proprietary source code to a commercial SaaS provider.

---

## How It Works

LegacyLens keeps apart three things that modernization tooling usually mixes together: what the compiler can prove, what an organization requires, and what a language model infers.

```text
      Reusable analysis and modernization core
                      ↓
      Organization-specific constraints      approved patterns, permitted and prohibited
                      ↓                      technologies, security and testing requirements
      Specialized analysis and AI-assisted capabilities
                      ↓
                  Validation
                      ↓
                 Human review
```

The core is deterministic and compiler-grounded, and stays reusable across organizations. Constraints are supplied as **policy rather than code**, so the same core adapts to a different environment without being rewritten — and a rule the framework cannot decide from static evidence is reported as *requiring human review*, never as a silent pass. AI-assisted capabilities operate inside those constraints, and their output is validated and reviewed by an engineer. The framework is not designed to modify mission-critical software autonomously.

### Pipeline stages

```text
┌─────────────────────────────────────────────────────────────┐
│  Layer 1 — Ingestion          MSBuildWorkspace loader        │
│  Layer 2 — Static Analysis    Roslyn SemanticModel + AppCAT  │
│  Layer 3 — AST Trace          Compiler-grounded LLM context  │
│  Layer 4 — Business Logic     Claude-powered rule extraction │
│  Layer 5 — Transformation     5-role agent pipeline          │
│  Layer 6 — Validation         Build + test + Git commit      │
└─────────────────────────────────────────────────────────────┘
```

### The Five-Role Agent Pipeline — planned, not implemented

Inspired by the VAPU architecture (Ala-Salmi et al., Tampere University, arXiv:2510.18509), which demonstrated a **22.5% improvement** in fulfilled modernization requirements over single-pass LLM approaches:

Manager → Prompt-Maker → Execution → Verification → Finalizer

- **Manager** — Decomposes the modernization strategy into ordered, dependency-respecting tasks
- **Prompt-Maker** — Builds Roslyn-grounded prompts with extracted business rules and compliance context
- **Execution** — Calls the Anthropic Claude API with semantic chunking and content-aware reasoning budgets
- **Verification** — Compiles output via Roslyn, runs existing test suites, evaluates on a 4-dimension rubric
- **Finalizer** — Performs targeted remediation on PARTIAL/FAIL verdicts (max 2 iterations); commits on PASS

---

## Key Features

### Implemented today

- **Roslyn-first semantics** — Analysis is built on compiler-accurate type information, symbol resolution and cross-project dependency edges, not on source text. Validated against three open-source solutions; see [Phase 1 in Action](#phase-1-in-action--validated-baselines).
- **Compiler-verified traces** — Each traced method carries its resolved signature, inheritance, call edges, state access, data flow and control-flow summary, plus a **grounding indicator** stating whether the compiler resolved every symbol. Unresolved names are never presented as real.
- **Organization constraints as policy** — A JSON policy of approved and prohibited technologies, minimum dependency versions and approved target frameworks is evaluated against the solution. Each rule is reported as **Compliant**, **Non-Compliant**, **Requires Human Review**, or **Not Applicable**; a rule that cannot be decided statically is never silently passed.
- **Modernization incident mapping** — Ingests Microsoft AppCAT reports and maps incidents onto the specific methods they occur in.
- **No Visual Studio required** — Stages the .NET Framework 2.0–4.8 reference assemblies itself, so legacy non-SDK solutions analyze on a bare .NET SDK install, including in Linux containers.
- **Deterministic and offline** — Byte-identical output across runs. The analysis core makes no network calls and emits no telemetry.

### Planned — see [Roadmap](#roadmap)

- **Business logic extraction** — A dedicated agent to extract and persist domain rules, invariants and integration contracts to SQLite *before* any transformation occurs.
- **Five-role transformation pipeline** — Manager → Prompt-Maker → Execution → Verification → Finalizer.
- **Compliance pattern library** — Roslyn `DiagnosticAnalyzer` + `CodeFixProvider` pairs targeting HIPAA Security Rule, PCI DSS v4.0, OWASP Top 10 for .NET, and SBOM generation (SPDX/CycloneDX).
- **Security-oriented analysis** — Detection of vulnerable dependencies, insecure patterns and modernization-related security concerns.
- **Provider-neutral LLM** — `IChatClient` abstraction over Anthropic Claude, Azure OpenAI, AWS Bedrock and locally-hosted models, for fully air-gapped deployment.

### Secure development alignment

Security is treated as part of the modernization lifecycle rather than a step performed after it. The constraint mechanism above is the first implemented piece of that approach: organizational security requirements — permitted technologies, minimum dependency versions, authentication requirements — are evaluated as policy and reported per rule. The design follows NIST SP 800-218 (Secure Software Development Framework), CISA Secure by Design, and the explicit *hard constraints* pattern described in NIST's 2026 work on agentic AI-assisted coding, under which non-negotiable validity requirements take priority over contextual instructions given to an agent. Security-oriented code analysis itself is planned, not implemented.

---

## Tech Stack

| Component | Technology |
|-----------|-----------|
| Runtime | .NET 10 LTS |
| Compiler API | Microsoft.CodeAnalysis (Roslyn) 5.x |
| LLM — Primary | Anthropic Claude (`claude-sonnet-4-6`) via `Anthropic.SDK` |
| LLM — Secondary | Azure OpenAI / AWS Bedrock (via `IChatClient`) |
| Agent Orchestration | Microsoft Agent Framework 1.0 |
| Static Analysis | AppCAT (.NET) + Custom Roslyn Analyzers |
| Persistence | SQLite + AzureSQL via `Microsoft.Data.Sqlite` + `Microsoft.Data.SqlClient` |
| Optional Graph DB | Neo4j (dependency graph + migration-wave ordering) |
| Version Control | LibGit2Sharp (per-change Git commits) |
| Containerization | Docker / .NET 10 base image |
| CI/CD | GitHub Actions + AzureDevops |

---

## Transformation Targets

- `.NET Framework 2.0–4.8` → `.NET 8 / .NET 10`
- `ASP.NET MVC 5` → `ASP.NET Core`
- `WCF` → `gRPC / Minimal API`
- `Entity Framework 6` → `EF Core`
- `Forms Authentication` → `OAuth 2.0 / OIDC`
- `Synchronous patterns` → `async/await`
- `Manual DI` → `Microsoft.Extensions.DependencyInjection`
"It is just the beginning..."

---

## Roadmap

| Phase | Timeline | Status |
|-------|----------|--------|
| **Phase 1** — Deterministic Core (MSBuildWorkspace + Roslyn graph + AppCAT + GitHub repo) | May–Aug 2026 | ✅ Complete — [validated on 3 OSS solutions](TestLegacyProjects/README.md); Layer 3 `TraceBuilder` and organization constraint evaluation delivered beyond the original scope |
| **Phase 2** — Business Logic Extraction (Anthropic API + SQLite persistence + arXiv preprint) | Aug 2026–Feb 2027 | 🟡 In Progress — persistence schema and provider-neutral client abstraction implemented; the extraction agent itself is not yet implemented |
| **Phase 3** — Full Transformation Pipeline (5-role agents + HIPAA/PCI DSS patterns + pilots) | Feb–Aug 2027 | ⬜ Planned |
| **Phase 4** — Production & Validation (Docker + enterprise pilots + academic publication) | Aug–Dec 2027 | ⬜ Planned |

Phase timelines follow the project's Technical Design Document.

---

## Academic Foundation

LegacyLens is grounded in peer-reviewed research:

- **MITRE / Diggs et al. (2024)** — arXiv:2411.14971. Establishes the documentation-first workflow and 4-dimension evaluation rubric (hallucination, completeness, readability, usefulness) adopted by LegacyLens's `BusinessLogicExtractorAgent`.
- **Tampere University / Ala-Salmi et al. (2025)** — arXiv:2510.18509 (VAPU). Validates the 5-role agent pipeline architecture; 22.5% improvement over zero-shot baselines.
- **InfoQ / Wytock, Judy, Foster Breilyn (2025)** — Introduces the Roslyn-based AST trace technique, explicitly chosen over tree-sitter and ANTLR for compiler-accurate C#/VB.NET type information. LegacyLens's `TraceBuilder` is a direct implementation.
- **Microsoft Azure-Samples / Legacy-Modernization-Agents (2025)** — MIT-licensed reference architecture for multi-agent modernization using Microsoft Agent Framework + Neo4j + SQLite. LegacyLens extends this pattern to .NET Framework → modern .NET with Roslyn as the semantic substrate.

---

## Getting Started

> **Phase 1 is complete.** The deterministic core — baseline reports, compiler-verified method traces, and organization constraint evaluation — is functional and validated against three open-source solutions. Phase 2 is in progress; no LLM-assisted capability is available yet.

```bash
# Clone the repository
git clone https://github.com/tiagorockman/legacylens.git
cd legacylens

# Build and test
dotnet build
dotnet test

# Run the Phase 1 baseline analysis on a solution
dotnet run --project src/LegacyLens.CLI -- analyze --solution /path/to/your.sln --output out

# Optionally map AppCAT incidents into the baseline
dotnet run --project src/LegacyLens.CLI -- analyze --solution /path/to/your.sln --appcat appcat-report.json --output out

# Evaluate the solution against an organization's own technical constraints
dotnet run --project src/LegacyLens.CLI -- analyze --solution /path/to/your.sln --constraints my-policy.json --output out
```

A constraint policy is plain JSON — approved and prohibited packages, minimum dependency versions, approved target frameworks, and requirements that must be verified by a person. See [TestLegacyProjects/example-constraints-policy.json](TestLegacyProjects/example-constraints-policy.json) for a worked example.

The `analyze` command produces:
- `out/solution-baseline.md` — project inventory, dependency graph (mermaid), complexity hotspots, deprecated symbols, and AppCAT incident roll-up
- `out/legacylens.db` — SQLite snapshot (`projects`, `symbols`, `reports` tables) used as the semantic substrate for later phases

### Compiler-verified traces (Layer 3)

The `trace` command selects the most complex methods and builds, for each one, the grounded context the LLM layers consume:

```bash
# Trace the 20 most complex methods, mapping AppCAT incidents onto them
dotnet run --project src/LegacyLens.CLI -- trace --solution /path/to/your.sln --appcat appcat-report.json --output out

# Or target specific code
dotnet run --project src/LegacyLens.CLI -- trace --solution /path/to/your.sln --project Billing --method Invoice --top 10
```

Each trace records the containing type and its inheritance chain, the resolved signature, calls split between solution code and framework, callers found across every project — including calls dispatched through interfaces — the domain state read and written, data flow, a control-flow graph summary, the AppCAT incidents inside the method, and the source split into token-bounded chunks at statement boundaries. A grounding flag reports whether the compiler resolved every symbol, so an unresolved name is never presented to the model as real.

- `out/trace-index.md` — ranked summary of every traced method
- `out/traces/NNN-Type.Method-hash.json` — structured trace for the LLM layers
- `out/traces/NNN-Type.Method-hash.md` — the same trace rendered as prompt context, for review

Output is deterministic: tracing the same solution twice produces byte-identical files.

Requirements:
- .NET 10 SDK — the only prerequisite. Visual Studio is **not** required, including for legacy non-SDK-style .NET Framework projects: LegacyLens stages the .NET Framework 2.0–4.8 reference assemblies itself, so a compiler-accurate load works on a bare SDK install and inside Linux containers.
- `nuget restore` for solutions using `packages.config` — `dotnet restore` does not support that format. LegacyLens detects the un-restored case and prints the fix at the top of the report.
- An Anthropic API key (`ANTHROPIC_API_KEY`) — Phase 2+ only; the Phase 1 baseline is fully deterministic and offline

---

## Phase 1 in Action — Validated Baselines

LegacyLens is validated end-to-end against three open-source solutions, two of them genuine legacy
.NET Framework codebases of the kind the project targets. Every run below was performed on a machine
with **no Visual Studio installed** — only the .NET 10 SDK.

| Solution | Target Framework | Projects | Types / Methods | Baseline |
|---|---|---:|---:|---:|
| [ContosoUniversityDotNetCore](https://github.com/jbogard/ContosoUniversityDotNetCore) | `netcoreapp2.0` | 2 | 163 / 205 | **2.5s** |
| [nopCommerce 3.90](https://github.com/nopSolutions/nopCommerce) | `.NET Framework 4.5.1` | 31 | 2,408 / 7,207 | **15.1s** |
| [SmartStoreNET 4.2.0](https://github.com/smartstore/SmartStoreNET) | `.NET Framework 4.7.2` | 25 | 4,142 / 13,968 | **18.7s** |

All **58 projects compile cleanly under Roslyn**, so the semantic model behind these baselines is
compiler-accurate rather than a syntax-only approximation — the property the whole trace-based
approach depends on. Every run is deterministic and offline: no LLM calls are involved in Phase 1.

Reproduction steps and the full generated reports live in
[TestLegacyProjects/](TestLegacyProjects/README.md).

```bash
dotnet run --project src/LegacyLens.CLI -- analyze --solution .\TestLegacyProjects\nopCommerce\src\NopCommerce.sln --appcat TestLegacyProjects\nopCommerce-appcat.json --output TestLegacyProjects\nopCommerce-out
```

```text
LegacyLens baseline complete in 15.1s
  Projects : 31 (0 cycle(s))
  Symbols  : 2408 types, 7207 methods
  AppCAT   : 1201 incident(s)
  Report   : out\solution-baseline.md
  Database : out\legacylens.db
```

### Complexity hotspots — where Phase 2 will start

The baseline ranks every method by cyclomatic complexity. In real legacy systems the top of that
list is where undocumented business rules accumulate, which is exactly what the
`BusinessLogicExtractorAgent` is being built to recover:

| Complexity | Method | Solution |
|---:|---|---|
| 174 | `SmartStore.Services.Search.LinqCatalogSearchService.GetProductQuery(...)` | SmartStoreNET |
| 163 | `Nop.Services.ExportImport.ImportManager.ImportProductsFromXlsx(Stream)` | nopCommerce |
| 157 | `SmartStore.Web.Controllers.CatalogHelper.PrepareProductDetailModel(...)` | SmartStoreNET |
| 122 | `SmartStore.Services.Orders.OrderProcessingService.PlaceOrder(...)` | SmartStoreNET |
| 110 | `Nop.Services.Common.PdfService.PrintOrdersToPdf(...)` | nopCommerce |
| 101 | `Nop.Services.Catalog.ProductService.SearchProducts(...)` — 27 parameters | nopCommerce |

### Compiler-verified traces

`trace` was run on the 20 most complex methods of each legacy solution:

| Solution | Methods traced | Fully resolved by the compiler | Source chunks | ~Tokens |
|---|---:|---:|---:|---:|
| nopCommerce 3.90 | 20 | **20 of 20** | 28 | 80,090 |
| SmartStoreNET 4.2.0 | 20 | **20 of 20** | 25 | 72,822 |

Every symbol in these 40 traces was resolved by Roslyn, so none of the type or member names handed to
the LLM layers can be hallucinated. Callers are found across project boundaries: nopCommerce's
`ProductService.SearchProducts` is traced to `CatalogModelFactory` in the web project, which reaches
it through the `IProductService` interface. Each solution traces in about 30 seconds, tracing the same
solution twice produces byte-identical output, and AppCAT incidents land on the exact method line
ranges — checked against an independent derivation from the raw AppCAT report, with zero mismatches.

### AppCAT integration

The nopCommerce run ingests a real report from Microsoft's `dotnet appcat`, mapping migration
incidents into the baseline alongside the Roslyn analysis:

| Severity | Incidents |
|---|---:|
| Potential | 919 |
| Optional | 276 |
| Mandatory | 6 |

1,201 incidents totalling **2,453 story points** of AppCAT-estimated effort, with the dominant rules
being hardcoded URLs (557), local or network I/O (280) and hardcoded sensitive data (195).

---

## Quick Links

| Resource | Description |
|-----------|-------------|
| 📄 [Executive Summary](docs/Executive%20Summary.pdf) | High-level overview |
| 📄 [Technical White Paper](docs/Technical%20White%20Paper.pdf) | Technical methodology |

## Contributing

Contributions are welcome. Please open an issue before submitting a pull request to discuss the proposed change.

Areas where contributions are especially valuable:
- Additional Roslyn analyzer rules for legacy pattern detection
- Compliance pattern library extensions (FedRAMP, CMMC, SOC 2)
- Integration tests against real OSS .NET Framework solutions
- Documentation and architecture diagrams

---

## License

MIT License — see [LICENSE](LICENSE) for details.

---

## Author

**Tiago Neves Sousa**
Senior Software Engineer | M.S. Computer Science Student, Texas Tech University
Independent Researcher in AI-Assisted Software Modernization

---

*LegacyLens is an independent open-source research project. It is not affiliated with or endorsed by Microsoft, Anthropic, or any other commercial entity.*
