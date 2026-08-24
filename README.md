# UpgradePilot

[![CI](https://github.com/qmmughal/upgradepilot-core/actions/workflows/ci.yml/badge.svg)](https://github.com/qmmughal/upgradepilot-core/actions/workflows/ci.yml)
[![License: Apache 2.0](https://img.shields.io/badge/License-Apache%202.0-blue.svg)](LICENSE)
[![Website](https://img.shields.io/badge/Website-upgradepilot.ai-0A66C2?style=flat)](https://upgradepilot.ai/)

**AI-powered software modernization for .NET, React, and Next.js.** UpgradePilot is an
open-source, multi-agent pipeline that automatically upgrades applications — including
**ASP.NET Zero**, **ABP Framework**, plain .NET, React, and Next.js, plus repos that mix
a .NET backend with a React/Next.js frontend — to newer framework versions while
preserving your customizations. It's built for teams stuck on years-old framework
versions because a manual upgrade would take weeks and risks silently breaking custom
business logic.

Instead of a single AI prompt rewriting your codebase, UpgradePilot runs a pipeline of
20 specialized agents — repository analysis, dependency and version detection,
Roslyn-based AST comparison and semantic three-way merge, build/test validation,
database migrations, and pull request generation — each with a defined contract,
explainable confidence score, and citations for every claim. Nothing overwrites your
code without evidence.

## Why UpgradePilot

- **Real tools, not guesses.** Package upgrades are verified by an actual `dotnet
  restore`/`npm install`, not assumed to work; target versions come from `dotnet list
  package --outdated`/`npm outdated`, not an LLM's memory. React/Next.js codemods run
  through the real `react-codemod`/`@next/codemod` CLIs, using transform names checked
  against those projects' own repos — a fabricated transform name would just fail at
  runtime, so nothing is guessed.
- **AST-based where it matters.** ASP.NET Zero/ABP template and code comparison use
  Roslyn syntax trees, so merges understand C# structure instead of guessing from text
  diffs.
- **Customer customizations are never silently overwritten.** The Semantic Merge
  Engine only auto-propagates a template change when your code never touched that
  member; anything else becomes an explicit, human-reviewable conflict.
- **Explainable by construction.** Every agent result carries a confidence score, a
  human-readable explanation, and citations — never a hidden decision.
- **Open core, not open-weak.** The same agents that run here also power the hosted
  commercial product — see [Open Core Boundary](docs/architecture/open-core-boundary.md).
  Nothing is held back from the free tier to make the paid tier look better.
- **Extensible via [MCP](https://modelcontextprotocol.io/).** A real Model Context
  Protocol server exposes repository, build, and test tools that any MCP-compatible
  AI client can call.

## What it does today

19 of 20 agents from the [architecture spec](docs/architecture/agents.md) are
implemented and tested (179 tests), spanning:

| Phase | Agents |
| --- | --- |
| Discovery | Repository Analyzer, Version Detector, Framework Detector (incl. React/Next.js/Mixed stack detection and Next.js Pages vs. App Router detection), Dependency Analyzer (.NET + npm), Repository Intelligence |
| Knowledge | Documentation Retrieval, Release Notes Intelligence, Template Downloader, Template Comparator, Semantic Merge Engine, Upgrade Planner |
| Execution | Package Upgrade (NuGet, npm, and Central Package Management), API Refactoring (Roslyn codemods), React/Next.js Codemods, Database Migration |
| Validation | Build Validation (.NET + frontend), Test Runner (.NET, Jest, Vitest), Security Validation |
| Delivery | Documentation Generator, Pull Request Generator, Rollback Planner |

Stack-specific execution is real end to end, not just detection:

| Stack | What actually runs |
| --- | --- |
| **.NET** | NuGet package bumps (verified by `dotnet restore`) or Central Package Management bumps, target framework upgrades, Roslyn rename codemods, EF Core migrations (destructive ops flagged, never silently applied), across every `.csproj` in a multi-project solution |
| **React** | `npm install`-verified package.json upgrades, `react-codemod` transforms, `npm run build` validation, Jest/Vitest test-summary parsing |
| **Next.js** | Same as React, plus `@next/codemod` transforms and Pages Router vs. App Router detection |
| **Mixed (.NET + React/Next.js)** | Backend upgrades and validates first, then the frontend runs through the same real pipeline; if the frontend can't be confidently identified, that half is flagged for manual review instead of guessed at |

Package target versions (`dotnet list package --outdated` / `npm outdated`) and React/
Next.js codemod selection (matched against each framework's own real GitHub release
notes) are derived automatically — a caller can still override either by hand.

Plus: SQLite-backed session persistence, a MassTransit saga for workflow
orchestration, and a real MCP server (`UpgradePilot.Core.Mcp`).

See [`docs/architecture/agents.md`](docs/architecture/agents.md) for the full spec of
every agent (purpose, inputs/outputs, retry strategy, validation rules) and
[`docs/architecture/open-core-boundary.md`](docs/architecture/open-core-boundary.md)
for what's open-source here versus reserved for UpgradePilot's hosted/commercial tier.

## Repository layout

```
src/
  UpgradePilot.Core.Domain/              Agent contract (IUpgradePilotAgent), AgentResult, UpgradeContext blackboard
  UpgradePilot.Core.Agents.Discovery/    Repository Analyzer, Version Detector, Framework Detector
  UpgradePilot.Core.Agents.Pipeline/     Knowledge/Execution/Validation/Delivery-phase agents
  UpgradePilot.Core.Persistence.Sqlite/  Local session persistence
  UpgradePilot.Core.Orchestration/       MassTransit saga (workflow orchestration)
  UpgradePilot.Core.Mcp/                 Model Context Protocol server
tests/
  (one test project per src project, mirroring the same structure)
docs/
  architecture/                          Architecture decisions, agent specs, diagrams
```

## Building

```
dotnet build
dotnet test
```

Requires the .NET 10 SDK. Some tests exercise the real npm toolchain (package
upgrades, codemods, build/test validation for React/Next.js), so Node.js must also be
installed for the full suite to pass.

## Contributing

See [`CONTRIBUTING.md`](CONTRIBUTING.md). This project follows the
[Contributor Covenant](CODE_OF_CONDUCT.md). Report security issues per
[`SECURITY.md`](SECURITY.md) — not as a public issue.

## License

Apache License 2.0 — see [`LICENSE`](LICENSE).
