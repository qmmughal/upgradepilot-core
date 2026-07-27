# UpgradePilot

[![CI](https://github.com/qmmughal/upgradepilot-core/actions/workflows/ci.yml/badge.svg)](https://github.com/qmmughal/upgradepilot-core/actions/workflows/ci.yml)
[![License: Apache 2.0](https://img.shields.io/badge/License-Apache%202.0-blue.svg)](LICENSE)

AI-powered software modernization platform. First supported target: **AspNet Zero**.

UpgradePilot combines semantic code analysis, AI reasoning, automated validation, and
release intelligence to reduce framework upgrades from weeks to hours — while
preserving customer customizations.

This repo, `upgradepilot-core`, is the open-source core: agent contracts, the upgrade
pipeline domain model, and everything needed to run the pipeline locally. See
[`docs/architecture/open-core-boundary.md`](docs/architecture/open-core-boundary.md)
for what's open-source versus reserved for UpgradePilot's commercial products, and
[`docs/architecture/agents.md`](docs/architecture/agents.md) for the full multi-agent
design.

## Status

Early architecture/scaffolding stage.

## Repository layout

```
src/
  UpgradePilot.Core.Domain/     Agent contract (IUpgradePilotAgent), AgentResult, UpgradeContext blackboard
tests/
  UpgradePilot.Core.Domain.Tests/
docs/
  architecture/              Architecture decisions and diagrams
```

## Building

```
dotnet build
dotnet test
```

Requires .NET 10 SDK.

## Contributing

See [`CONTRIBUTING.md`](CONTRIBUTING.md). This project follows the
[Contributor Covenant](CODE_OF_CONDUCT.md). Report security issues per
[`SECURITY.md`](SECURITY.md) — not as a public issue.

## License

Apache License 2.0 — see [`LICENSE`](LICENSE).
