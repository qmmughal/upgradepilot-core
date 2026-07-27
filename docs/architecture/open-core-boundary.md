# Open Core Boundary

Status: Draft v0.1 · Owner: CTO/Architecture

Defines what lives in the open-source `upgradepilot-core` repo versus what's reserved for
commercial UpgradePilot products (Cloud/Enterprise/Studio), and the technical seam that
lets the two evolve independently without forking.

## 1. Principle: split by delivery mode, not by capability

The temptation with open core is to ship a deliberately weaker OSS product and hold
back the good agents for the paid tier. We reject that — it contradicts two of our
core values (**Community over closed ecosystems**, **Correctness over speed**) and it
undermines the thing that makes UpgradePilot trustworthy: you can read, run, and audit
every agent that touches your code, for free, forever.

Instead, the boundary is **delivery mode**, not agent capability:

| | Open Source (`upgradepilot-core` + friends) | Premium (Cloud/Enterprise/Studio) |
|---|---|---|
| Where it runs | Locally, via `upgradepilot-cli`, in your own process | Hosted, multi-tenant, UpgradePilot-operated infrastructure |
| Scope | One repo, one user, one run at a time | Many repos/orgs, scheduled, concurrent |
| LLM billing | Bring-your-own API key | Metered, included |
| State | Ephemeral / local file (`.upgradepilot/session.db`) | Persisted, multi-tenant Postgres, cross-run history feeds Episodic memory |
| Trigger | Manual CLI invocation | Webhooks, schedules, dashboard, API |
| Identity | Your local git/GitHub credentials | Enterprise SSO/SAML, org roles |
| Integrations | GitHub (via your own `gh` auth) | GitHub Enterprise, GitLab, Bitbucket, Azure DevOps, CI/CD automation |
| Observability | Local OpenTelemetry console/file exporter | Org dashboards, audit logs, usage analytics |

All 20 agents from `docs/architecture/agents.md` — including Template Comparator,
Semantic Merge Engine, and Pull Request Generator — run in both modes, unmodified.
Premium is the hosted, team-scale operating model around the same agents, not a
different, better set of agents.

**Concretely, this means:** a solo developer running `upgradepilot upgrade` against their
own AspNet Zero repo gets the identical merge/refactor/validation pipeline an
Enterprise customer gets. What they don't get for free is: someone else's
infrastructure running it on a schedule across 200 repos, SSO, an audit trail for
compliance, and a support contract.

## 2. Licensing

`upgradepilot-core`, `upgradepilot-cli`, `upgradepilot-sdk`, `upgradepilot-mcp`,
`upgradepilot-analyzers`, `upgradepilot-roslyn`, `upgradepilot-docs`, `upgradepilot-examples`,
`awesome-upgradepilot` are **Apache License 2.0**.

**Why Apache-2.0 over MIT:** Apache-2.0's explicit patent grant and termination clause
matters here specifically because this platform performs automated code
transformation — enterprise legal teams reviewing whether to let an AI agent rewrite
their codebase will ask about patent exposure and license clarity before they ask
about features. MIT is simpler but silent on patents; for a dev-tools company
targeting enterprise adoption, that silence is a sales friction point, not a
simplification win. Precedent: Kubernetes, Terraform (pre-BSL), most CNCF projects use
Apache-2.0 for the same reason.

## 3. The technical seam

Everything premium needs to plug into is expressed as a port (interface) in
`upgradepilot-core`, never as a hook baked into a specific agent:

- **`IUpgradePilotAgent<TInput,TOutput>`** (exists today, `src/UpgradePilot.Core.Domain`) —
  the same agent binary runs in the CLI process or inside a Cloud worker. Cloud does
  not reimplement or fork agents; it references the OSS package and hosts it.
- **`UpgradeContext` is storage-agnostic in-process today; persistence is the seam.**
  The OSS CLI's default store is local (file/SQLite) — good enough for a single
  session. A hosted `IUpgradeContextStore` port (planned, not yet implemented — see
  `docs/issues/next-steps.md` issue #2) is what Cloud implements against a multi-tenant
  Postgres cluster with org/repo scoping. Same port, swappable adapter — this is the
  Repository Pattern doing its actual job, not decoration.
- **MCP tool namespaces are the integration seam.** OSS ships `repo.*`/`git.*`/etc.
  against local sandboxed worktrees; Cloud's MCP tool implementations point at hosted
  sandboxes and add GitHub Enterprise/GitLab/Bitbucket/ADO connectors — same tool
  contracts, different backends, registered the same way a community contributor
  would register a new framework adapter.
- **No agent in `upgradepilot-core` may take a hard dependency on anything under the
  `UpgradePilot.Cloud.*`/`UpgradePilot.Enterprise.*` namespace.** The dependency direction is
  one-way: premium depends on core, never the reverse. This is enforced by premium
  code living in a separate, private repository (`upgradepilot-cloud`), not a separate
  folder in this one — a private folder in a public repo is a leak waiting to happen.

## 4. Repo topology

```
github.com/upgradepilot/            (org, public repos — community-owned)
├── upgradepilot-core                Domain contracts, agent implementations, saga
├── upgradepilot-cli                 Local CLI host (wires agents + local stores)
├── upgradepilot-sdk                 Client libraries for third-party integration
├── upgradepilot-mcp                 MCP tool server (local/sandboxed implementations)
├── upgradepilot-analyzers           Roslyn analyzers usable standalone
├── upgradepilot-roslyn              Roslyn-specific codemod infrastructure
├── upgradepilot-docs                Documentation site
├── upgradepilot-examples            Sample AspNet Zero repos + upgrade walkthroughs
└── awesome-upgradepilot              Community-curated adapters/rules/prompts

(private, UpgradePilot-owned)
└── upgradepilot-cloud                Hosted orchestrator, dashboards, auth, integrations
                                    References upgradepilot-core/-mcp as packages.
```

Today's local repo (`qmmughal/upgradepilot-core`, public) corresponds to the first item
above. It should move under the `upgradepilot` GitHub org once that org exists; until
then it's the working copy.

## 5. What's explicitly NOT in scope for `upgradepilot-core`

To keep the boundary honest, things that must never land in the OSS repo even as
disabled/stub code: auth/session handling beyond "use your local `gh`/git
credentials," multi-tenant data models, billing/metering, dashboard UI, enterprise
connector credentials or OAuth app secrets. If a feature needs any of those, it's a
`upgradepilot-cloud` feature by definition, not a feature-flagged part of core.

## 6. Open questions

1. Should `upgradepilot-cli`'s local `UpgradeContext` store be SQLite (portable, zero
   setup) or a flat append-only JSON log (simplest, human-diffable, matches the
   append-only fact model exactly)? Leaning JSON log for v0 — SQLite if session
   querying gets complex.
2. At what point does `upgradepilot-cloud` get scaffolded? Recommend after the Template
   Comparator/Semantic Merge Engine spike (`docs/issues/next-steps.md` issue #1)
   de-risks the core pipeline — no point building hosted infrastructure around agents
   whose core algorithm is still unvalidated.
