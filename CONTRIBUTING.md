# Contributing to upgradepilot-core

Thanks for considering a contribution. This repo is the open-source core of
UpgradePilot — see [`docs/architecture/open-core-boundary.md`](docs/architecture/open-core-boundary.md)
for what belongs here versus in UpgradePilot's commercial products.

## What belongs in this repo

Agent contracts and implementations, the upgrade pipeline domain model, CLI/MCP tool
integration, Roslyn analyzers, and anything runnable locally by a single developer.
Anything requiring hosted multi-tenant infrastructure, billing, or enterprise auth
does not belong here — see the boundary doc before proposing that kind of change.

## Getting started

Requires the .NET 10 SDK.

```
git clone https://github.com/qmmughal/upgradepilot-core.git
cd upgradepilot-core
dotnet build
dotnet test
```

## Making a change

1. Open an issue first for anything beyond a small fix — especially new agents, since
   each one should be specified (purpose, inputs/outputs, memory, MCP tools, retry
   strategy, validation rules, success criteria) per
   [`docs/architecture/agents.md`](docs/architecture/agents.md) before implementation.
2. Fork and branch from `main`.
3. Write tests for new behavior. `dotnet test` must pass.
4. Keep PRs scoped to one change — easier to review, easier to revert.
5. Open a pull request using the PR template; link the issue it closes.

## Adding a new agent or framework adapter

This is the primary community extension point. New agents implement
`IUpgradePilotAgent<TInput, TOutput>` (`src/UpgradePilot.Core.Domain/Agents`). New framework
adapters (e.g. targeting Spring Boot or Laravel) reuse the Discovery/Validation/
Delivery agents and replace only the Template/Refactoring/Migration agents for that
framework — see `docs/architecture/agents.md` §5.

## Code style

Follow the conventions already in the codebase: Clean Architecture layering, no
regex-based transformations where an AST-based one (Roslyn/tree-sitter) is possible,
no placeholder/stub implementations merged to `main`.

## Reporting bugs and security issues

Bugs: open a GitHub issue. Security vulnerabilities: see [`SECURITY.md`](SECURITY.md)
— do not open a public issue for those.

## Code of Conduct

This project follows the [Contributor Covenant](CODE_OF_CONDUCT.md).
