# LegacyLens 🔍

> **AI-Assisted .NET/C# Legacy Modernization Agent**

![Status](https://img.shields.io/badge/status-Phase%201%20In%20Progress-yellow)
![License](https://img.shields.io/badge/license-MIT-blue)
![Platform](https://img.shields.io/badge/platform-.NET%208-purple)
![LLM](https://img.shields.io/badge/LLM-Anthropic%20Claude-orange)
![Build](https://img.shields.io/badge/build-passing-brightgreen)

LegacyLens is an open-source, AI-assisted modernization agent built in **C#/.NET 8** that helps organizations analyze, refactor, and migrate legacy .NET Framework codebases into secure, cloud-ready architectures aligned with modern compliance requirements (HIPAA, PCI DSS, FedRAMP, CMMC).

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

LegacyLens implements a **six-layer architecture** that separates deterministic compiler analysis from probabilistic LLM reasoning:

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

### The Five-Role Agent Pipeline

Inspired by the VAPU architecture (Ala-Salmi et al., Tampere University, arXiv:2510.18509), which demonstrated a **22.5% improvement** in fulfilled modernization requirements over single-pass LLM approaches:

Manager → Prompt-Maker → Execution → Verification → Finalizer

- **Manager** — Decomposes the modernization strategy into ordered, dependency-respecting tasks
- **Prompt-Maker** — Builds Roslyn-grounded prompts with extracted business rules and compliance context
- **Execution** — Calls the Anthropic Claude API with semantic chunking and content-aware reasoning budgets
- **Verification** — Compiles output via Roslyn, runs existing test suites, evaluates on a 4-dimension rubric
- **Finalizer** — Performs targeted remediation on PARTIAL/FAIL verdicts (max 2 iterations); commits on PASS

---

## Key Features

- **Roslyn-First Semantics** — Every LLM prompt is grounded with compiler-accurate type information, symbol resolution, control/data-flow graphs, and cross-project dependency edges. No hallucinated namespaces or NuGet packages.
- **Business Logic Extraction** — A dedicated agent extracts and persists domain rules, invariants, and integration contracts to SQLite *before* any transformation occurs, preserving institutional knowledge that conventional migrations lose.
- **Compliance Pattern Library** — Roslyn `DiagnosticAnalyzer` + `CodeFixProvider` pairs targeting HIPAA Security Rule, PCI DSS v4.0, OWASP Top 10 for .NET, and SBOM generation (SPDX/CycloneDX).
- **Provider-Neutral LLM** — `IChatClient` abstraction supports Anthropic Claude (default), Azure OpenAI, AWS Bedrock, and locally-hosted models. Deploy in fully air-gapped VPC environments.
- **Reproducible Runs** — Every run is pinned to a model snapshot, deterministic chunking strategy, and full audit log committed to Git.
- **On-Premises Deployable** — Docker-packaged with no telemetry by default. Designed for FedRAMP-bound agencies and HIPAA covered entities.

---

## Tech Stack

| Component | Technology |
|-----------|-----------|
| Runtime | .NET 8 LTS |
| Compiler API | Microsoft.CodeAnalysis (Roslyn) 4.x |
| LLM — Primary | Anthropic Claude (`claude-sonnet-4-6`) via `Anthropic.SDK` |
| LLM — Secondary | Azure OpenAI / AWS Bedrock (via `IChatClient`) |
| Agent Orchestration | Microsoft Agent Framework 1.0 |
| Static Analysis | AppCAT (.NET) + Custom Roslyn Analyzers |
| Persistence | SQLite + AzureSQL via `Microsoft.Data.Sqlite` + `Microsoft.Data.SqlClient` |
| Optional Graph DB | Neo4j (dependency graph + migration-wave ordering) |
| Version Control | LibGit2Sharp (per-change Git commits) |
| Containerization | Docker / .NET 8 base image |
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
| **Phase 1** — Deterministic Core (MSBuildWorkspace + Roslyn graph + AppCAT + GitHub repo) | May–Aug 2026 | 🟡 In Progress |
| **Phase 2** — Business Logic Extraction (Anthropic API + SQLite persistence + arXiv preprint) | Aug–Nov 2026 | ⬜ Planned |
| **Phase 3** — Full Transformation Pipeline (5-role agents + HIPAA/PCI DSS patterns + pilots) | Nov 2026–Mar 2027 | ⬜ Planned |
| **Phase 4** — Production & Validation (Docker + enterprise pilots + academic publication) | Mar–Aug 2027 | ⬜ Planned |

---

## Academic Foundation

LegacyLens is grounded in peer-reviewed research:

- **MITRE / Diggs et al. (2024)** — arXiv:2411.14971. Establishes the documentation-first workflow and 4-dimension evaluation rubric (hallucination, completeness, readability, usefulness) adopted by LegacyLens's `BusinessLogicExtractorAgent`.
- **Tampere University / Ala-Salmi et al. (2025)** — arXiv:2510.18509 (VAPU). Validates the 5-role agent pipeline architecture; 22.5% improvement over zero-shot baselines.
- **InfoQ / Wytock, Judy, Foster Breilyn (2025)** — Introduces the Roslyn-based AST trace technique, explicitly chosen over tree-sitter and ANTLR for compiler-accurate C#/VB.NET type information. LegacyLens's `TraceBuilder` is a direct implementation.
- **Microsoft Azure-Samples / Legacy-Modernization-Agents (2025)** — MIT-licensed reference architecture for multi-agent modernization using Microsoft Agent Framework + Neo4j + SQLite. LegacyLens extends this pattern to .NET Framework → modern .NET with Roslyn as the semantic substrate.

---

## Getting Started

> ⚠️ **Phase 1 is actively in development.** The project is not yet feature-complete. Architecture documentation, design documents, and the Phase 1 analysis core are the current focus.

```bash
# Clone the repository
git clone https://github.com/tiagorockman/legacylens.git
cd legacylens

# Restore dependencies
dotnet restore

# Build
dotnet build

# Run baseline analysis on a solution (Phase 1)
dotnet run --project src/LegacyLens.CLI -- analyze --solution /path/to/your.sln
```

Requirements:
- .NET 8 SDK
- An Anthropic API key (set as `ANTHROPIC_API_KEY` environment variable)
- MSBuild / Visual Studio Build Tools (for `MSBuildWorkspace`)

---

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
