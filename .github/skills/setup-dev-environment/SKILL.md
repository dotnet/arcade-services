---
name: setup-dev-environment
description: 'Set up an arcade-services local development environment from zero by following docs/DevGuide.md. Use when asked to "set up my dev environment", "bootstrap local development", "get me running locally", or "configure the repo for local dev". Runs the automatable steps, verifies prerequisites, and flags the human-gated ones.'
---

# Set Up the Local Development Environment

Walks a contributor through getting the Product Construction Service (PCS) and DARC running
locally, following [`docs/DevGuide.md`](../../../docs/DevGuide.md). The skill **runs the steps it
can**, **verifies prerequisites**, and **clearly flags steps that require a human** (installs that
need a GUI, access grants, machine-specific paths). It is a guided checklist, not a magic one-shot
installer.

## When to Use

- A new contributor wants a working local environment from scratch.
- An existing checkout needs the database, EF migrations, or `launchSettings.json` configured.
- You want to verify which prerequisites are still missing.

## Ground Rules

- `docs/DevGuide.md` is the source of truth. If it changes, prefer it over this skill and update
  this file.
- **Never invent or guess secrets, tokens, KeyVault values, or machine-specific paths.** Prompt the
  user for VMR / `TmpPath` locations instead of assuming them.
- Run one step at a time and confirm it succeeded before moving on; report failures with the exact
  command and error.
- Use the global `dotnet` command for build/test/tool commands.

## Steps That Require a Human (verify, then flag — do NOT attempt to automate)

Surface these early as a checklist and let the user confirm each one. The skill cannot complete
them:

1. **Install Visual Studio (Preview)** with the `Azure Development => .NET Aspire SDK (Preview)`
   workload and `ASP.NET and web development => .NET 8.0/9.0 WebAssembly Build Tools`.
2. **Install Docker Desktop** (https://www.docker.com/products/docker-desktop).
3. **Install SQL Server Express**
   (https://www.microsoft.com/en-us/sql-server/sql-server-downloads).
4. **`git config --system core.longpaths true`** — needs an **elevated** shell, so the user must run
   it themselves (the skill can run the `--global` variant; see below).
5. **Join the `maestro-auth-test` GitHub org** — someone must add the user manually.
6. **Get read access to the `ProductConstructionDev` KeyVault** — someone must grant it.

For each, verify whether it is already satisfied where possible (e.g. `docker info`, `dotnet --info`,
checking a `sqlcmd`/SQL connection) and report ✅ / ❌ with the remediation from the list above.

## Steps the Skill Can Run

### Step 1: Configure git long paths (global)
```ps1
git config --global core.longpaths true
```
Remind the user to also run the `--system` variant from an elevated shell (human-gated, above).

### Step 2: Install the EF Core CLI
```ps1
dotnet tool install --global dotnet-ef
```
If already installed, `dotnet ef --version` confirms it; skip the install.

### Step 3: Build `Maestro.Data`
```ps1
dotnet build src\Maestro\Maestro.Data\Maestro.Data.csproj
```

### Step 4: Create / update the local database
The generated obj files live in the **root** `artifacts` folder, not the project's `obj` folder.
Resolve the absolute obj path first, then run from `src\Maestro\Maestro.Data`:
```ps1
cd src\Maestro\Maestro.Data
dotnet ef --msbuildprojectextensionspath <repo-root>\artifacts\obj\Maestro.Data\ database update
```
<!-- TODO: confirm the resolved obj path exists before running; the trailing slash matters -->

### Step 5: Seed the `Repositories` table
This requires the local SQL Express DB (human-gated install above) to be reachable. Insert the rows
from the DevGuide (do not alter the values):
```sql
INSERT INTO [Repositories] (RepositoryName, InstallationId) VALUES
    ('https://github.com/maestro-auth-test/maestro-test', 289474),
    ('https://github.com/maestro-auth-test/maestro-test2', 289474),
    ('https://github.com/maestro-auth-test/maestro-test3', 289474),
    ('https://github.com/maestro-auth-test/maestro-test-vmr', 289474),
    ('https://github.com/maestro-auth-test/arcade', 289474),
    ('https://github.com/maestro-auth-test/dnceng-vmr', 289474);
```
The DevGuide does this via SQL Server Object Explorer in VS; the same SQL can be run with `sqlcmd`
against the local SQLEXPRESS instance.

### Step 6: Configure `launchSettings.json` (prompt for machine-specific paths)
Ask the user for their `TmpPath` (and reuse an existing VMR TMP folder if they have one), then write:
- `src\ProductConstructionService\ProductConstructionService.AppHost\Properties\launchSettings.json`
- `src\ProductConstructionService\ProductConstructionService.Api\Properties\launchSettings.json`

Use the exact templates from `docs/DevGuide.md` ("Configuring the service for local runs"), filling
in only the user-provided paths. **Never hardcode another machine's paths.**

## Validation

- `docker info` succeeds (Docker Desktop running).
- `dotnet build` of the solution / `Maestro.Data` succeeds (warnings are errors in this repo).
- `dotnet ef ... database update` reports `Done.` and the `Repositories` rows exist.
- Optionally run the service: set `ProductConstructionService.AppHost` as startup project (or
  `dotnet run` from `src\ProductConstructionService\ProductConstructionService.AppHost`) and confirm
  it starts.

## Output / Hand-off

End with a checklist summarizing each step as ✅ done, ⚠️ needs user action (with the exact
remediation), or ❌ failed (with the command + error), so the user knows precisely what remains.
