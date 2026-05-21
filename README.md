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
