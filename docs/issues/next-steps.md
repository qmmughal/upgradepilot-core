# Next-step issue drafts

All 23 issues below have been filed against
[qmmughal/upgradepilot-core](https://github.com/qmmughal/upgradepilot-core/issues) (#1-#23).
This file is kept as the source-of-truth draft — if an issue needs rewording, edit it
here first, then update the GitHub issue to match.

---

## 1. Prototype Template Comparator + Semantic Merge Engine against real repos

**Labels:** `spike`, `high-risk`, `agent-spec`

**Body:**
Highest-uncertainty component in the upgrade pipeline (see
`docs/architecture/agents.md` §6, risk 2). Before building the rest of the pipeline,
validate that AST-level three-way merge can propagate template changes into heavily
customized AspNet Zero repos without silently destroying customer business logic.

Acceptance criteria:
- Run against 2–3 representative AspNet Zero repos with real-world customization levels.
- Report conflict rate and categorize conflict types (structural vs. semantic).
- Recommendation: proceed as designed, or revise the merge strategy.

---

## 2. Stand up `UpgradeContext` schema and `IUpgradePilotAgent` contract in upgradepilot-core

**Labels:** `foundation`

**Body:**
Done locally as of this scaffold — `src/UpgradePilot.Core.Domain` contains
`IUpgradePilotAgent<TInput,TOutput>`, `AgentResult<TOutput>`, `ValidationResult`,
`RetryPolicy`, and the append-only `UpgradeContext` blackboard. Remaining work once a
real repo exists:
- Persist `UpgradeContext`/`UpgradeContextFact` to Postgres (currently in-memory only).
- Add Redis caching layer per architecture doc §1.2.
- Wire up `UpgradeSaga` (MassTransit state machine) per §1.1.

---

## 3. Stand up upgradepilot-mcp with the Discovery-phase tool set

**Labels:** `foundation`, `mcp`

**Body:**
Implement `repo.*` and `git.*` MCP tools (`repo.clone`, `repo.listProjects`,
`repo.stat`, `repo.readFile`, `repo.grep`, `git.log`, `git.blame`, `git.diffStat`) —
enough to ship Repository Analyzer through Repository Intelligence (agents #1–5 in
`docs/architecture/agents.md` §4) as a vertical slice end to end.

Acceptance criteria:
- Each tool sandboxed per session (isolated container + git worktree — see §1.6).
- Discovery-phase agents implementable and testable against the tool set.

---

## 4. Agent implementation issues (one per agent)

**Labels:** `agent-spec`

Template — file one issue per row using this shape:

> **Title:** Implement `<Agent Name>` agent
> **Body:** Implement per spec in `docs/architecture/agents.md` §4.\<n\> — Purpose,
> Responsibilities, Inputs/Outputs, Memory, MCP tools, Retry strategy, Validation
> rules, Success criteria all defined there. Link this issue back to that section.

| # | Agent | Phase |
|---|---|---|
| 1 | Repository Analyzer | Discovery |
| 2 | Version Detector | Discovery |
| 3 | Framework Detector | Discovery |
| 4 | Dependency Analyzer | Discovery |
| 5 | Repository Intelligence | Discovery |
| 6 | Documentation Retrieval | Knowledge |
| 7 | Release Notes Intelligence | Knowledge |
| 8 | Template Downloader | Knowledge |
| 9 | Template Comparator | Knowledge |
| 10 | Semantic Merge Engine | Knowledge |
| 11 | Upgrade Planner | Knowledge |
| 12 | Package Upgrade Agent | Execution |
| 13 | API Refactoring Agent | Execution |
| 14 | Database Migration Agent | Execution |
| 15 | Build Validation Agent | Validation |
| 16 | Test Runner Agent | Validation |
| 17 | Security Validation Agent | Validation |
| 18 | Documentation Generator | Delivery |
| 19 | Pull Request Generator | Delivery |
| 20 | Rollback Planner | Cross-cutting |

Suggested order: #1–5 first (needed for the vertical slice in issue #3 above), then
#9–11 (blocked on the Template Comparator spike in issue #1), then the rest.
