# AGENTS.md — Product Construction Service (PCS)

Scope: `src/ProductConstructionService/`. Extends the root AGENTS.md.

## Running locally
- Run the service via the Aspire AppHost: `dotnet run --project ProductConstructionService.AppHost`.
- Requires Docker Desktop running. Do not assume a plain `dotnet run` on the Api project is sufficient.

## Conventions
- The API uses Newtonsoft.Json with camelCase properties and camelCase string enums; dates are ISO-8601 UTC (`yyyy-MM-ddTHH:mm:ssZ`). Match this when adding contracts.
- API controllers live in `ProductConstructionService.Api/Api/`.

## Safety
- `test/ProductConstructionService.ScenarioTests` require a fully deployed service — never run them locally or in agent verification.
