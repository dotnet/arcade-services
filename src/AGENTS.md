# AGENTS.md — `src/` project map

Scope: `src/`. Extends the root AGENTS.md. A brief orientation of what lives where; much dependency-flow work spans several of these projects.

## DARC CLI — `Microsoft.DotNet.Darc/`
- `Darc` — the DARC command-line tool (verbs = Operations + CommandLineOptions pairs).
- `DarcLib` — the core dependency-flow library (VMR / codeflow, git operations, version files) shared by DARC and PCS.

## Maestro libraries — `Maestro/`
(Former Maestro service; now shared libraries consumed by PCS.)
- `Maestro.Data` — EF Core data layer and the Build Asset Registry (BAR) `DbContext` + migrations.
- `Maestro.DataProviders` — BAR data-access implementations (SQL BAR client, remote/token factories).
- `Maestro.Common` — shared utilities (git URL helpers, caching, logging, version constants).
- `Maestro.Services.Common` — shared host/service wiring (database, Key Vault, data protection, service defaults).
- `Maestro.WorkItems` — background work-item and reminder infrastructure.
- `Maestro.MergePolicies` / `Maestro.MergePolicyEvaluation` — merge policy definitions and their evaluation logic/models.
- `Microsoft.DotNet.Maestro.Tasks` — MSBuild tasks used by builds to publish build/asset metadata to the BAR.

## Product Construction Service — `ProductConstructionService/`
See `ProductConstructionService/AGENTS.md` for run/convention details.
- `ProductConstructionService.Api` — main service host (ASP.NET Core API + background workers, Dockerized).
- `ProductConstructionService.AppHost` — .NET Aspire orchestration for running the service locally.
- `ProductConstructionService.DependencyFlow` — core dependency-flow logic: subscription triggering, PR updaters/targets, merge policy evaluation.
- `ProductConstructionService.SubscriptionTriggerer` — job that triggers subscriptions on a schedule.
- `ProductConstructionService.FeedCleaner` — job that cleans up stale package feeds.
- `ProductConstructionService.BarViz` — Blazor web UI for visualizing the Build Asset Registry.
- `Microsoft.DotNet.ProductConstructionService.Client` — generated client for the PCS API; regenerate when API contracts change.
