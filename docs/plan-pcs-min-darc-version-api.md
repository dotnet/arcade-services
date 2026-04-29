# Plan: PCS minimum darc-version enforcement — API side

Server-side counterpart to docs/plan-pcs-min-darc-version-client.md. Adds a middleware that rejects too-old darc clients with `426 Upgrade Required`, an admin-only controller for managing the minimum, and a pcs-cli command set. Client side is already implemented; do not modify generated client code.

**Wire contract recap**
- Request headers (always sent by new Client lib): `X-Client-Name`, `X-Client-Version`.
- Reject with `426 Upgrade Required`, response header `X-Minimum-Client-Version: <semver>` (when known), body = `ApiError` with per-case message.
- Storage: Redis key `min-client-version-darc` (single global, no TTL). Missing key = no enforcement (with warning log).

---

## Phase 1 — Middleware: client-version enforcement

**Goal:** reject `/api/*` requests from too-old darc clients before authentication runs.

**Order (decision tree, first-match wins):**
1. Path doesn't match `/api/*` → pass through.
2. `X-Client-Name` or `X-Client-Version` header missing → pass through (no enforcement when client identity is unknown).
3. `X-Client-Name != "darc"` → pass through (only darc is enforced).
4. `X-Client-Version` ends with `-dev` (case-insensitive suffix) → pass through (local dev builds).
5. Read `min-client-version-darc` from Redis. If missing → log warning ("minimum client version not configured in Redis"), pass through. If lookup throws → log warning, fail-open pass through.
6. Parse `X-Client-Version` as `NuGetVersion`. If parse fails → **426** (case: unparseable).
7. Parse the Redis value as `NuGetVersion`. If parse fails → log error, pass through (bad config, don't lock everyone out).
8. If client < min → **426** (case: too-old).

**Rejection response shape:**
- Status `426`.
- Header `X-Minimum-Client-Version: <semver>` when Redis value is known and parseable.
- JSON body = `ApiError` with per-case message:
  - unparseable: "Client version '{value}' could not be parsed. Please upgrade your darc client."
  - too-old: "Your darc version {client} is below the minimum required version {min}. Run `darc-init` (or `dotnet tool update -g microsoft.dotnet.darc`) to upgrade."
- Short-circuit (do not call `next`).

**Implementation:**
1. New file `src/ProductConstructionService/ProductConstructionService.Api/Configuration/ClientVersionEnforcementMiddleware.cs`. Class `ClientVersionEnforcementMiddleware` with `InvokeAsync(HttpContext, RequestDelegate)`. Inject `IRedisCacheFactory` and `ILogger<ClientVersionEnforcementMiddleware>`. Use `redisCacheFactory.Create(MinClientVersionConstants.DarcMinVersionKey).GetAsync()`.
2. Constants in `src/ProductConstructionService/ProductConstructionService.Api/Configuration/MinClientVersionConstants.cs`:
   - `DarcMinVersionKey = "min-client-version-darc"`
   - `ClientNameHeader = "X-Client-Name"`
   - `ClientVersionHeader = "X-Client-Version"`
   - `MinimumVersionResponseHeader = "X-Minimum-Client-Version"`
   - `DarcClientName = "darc"`
   - `DevVersionSuffix = "-dev"`
3. Register middleware in `src/ProductConstructionService/ProductConstructionService.Api/Program.cs` immediately **before** `app.UseAuthentication()` (currently line 65). Use `app.UseMiddleware<ClientVersionEnforcementMiddleware>()`.
4. `NuGet.Versioning` dependency: confirm transitive availability in the API project; add a direct reference if not.

**Key types to reuse:**
- `Maestro.Services.Common.Cache.IRedisCacheFactory` / `IRedisCache` (string-based `GetAsync()` / `SetAsync(value, expiration?)` / `TryDeleteAsync()`).
- `ProductConstructionService.Api.Api.ApiError` (constructor takes `string message`).
- `NuGet.Versioning.NuGetVersion.TryParse`.

---

## Phase 2 — Admin controller: `ClientVersionController`

**Goal:** admin-only HTTP API for the pcs-cli to read/set/clear the Redis minimum.

**Endpoints (all admin-only, all under route `client-version`):**
- `GET client-version/min-darc` → `200 { minimumVersion: "x.y.z" }` or `204 NoContent` if unset.
- `PUT client-version/min-darc` body `{ minimumVersion: "x.y.z" }` → `200`. Validate `NuGetVersion.TryParse`; on failure return `400 ApiError`.
- `DELETE client-version/min-darc` → `204` whether or not the key existed.

**Implementation:**
1. New file `src/ProductConstructionService/ProductConstructionService.Api/Controllers/ClientVersionController.cs`.
   - `[Route("client-version")]`, `[ApiVersion("2020-02-20")]`, class-level `[Authorize(Policy = AuthenticationConfiguration.AdminAuthorizationPolicyName)]`.
   - Inject `IRedisCacheFactory`. In each action create the cache: `var cache = _factory.Create(MinClientVersionConstants.DarcMinVersionKey);`.
   - Use `[SwaggerApiResponse]` attributes consistent with `StatusController`.
2. New DTO `MinClientVersionRequest` and `MinClientVersionResponse` records (or reuse one with nullable `MinimumVersion`). Place under `src/ProductConstructionService/ProductConstructionService.Api/Api/` next to other API DTOs.
3. Validation: explicit `NuGetVersion.TryParse` check at controller entry; return `BadRequest(new ApiError("Invalid version: '...'"))` on failure.

**Note:** Generated client code regeneration is the user's responsibility (per request, do not touch generated parts).

---

## Phase 3 — pcs-cli operations

**Goal:** three flat-verb operations that call the new admin endpoints. No interactive confirmation.

**Verbs (matching existing pattern from `GetPcsStatusOperation`):**
- `get-min-darc-version` — calls `GET client-version/min-darc`. Logs current value or "not set".
- `set-min-darc-version <version>` — positional arg. Calls `PUT`. Logs success.
- `clear-min-darc-version` — calls `DELETE`. Logs success.

**Implementation:**
1. New files under `tools/ProductConstructionService.Cli/Options/`:
   - `GetMinDarcVersionOptions.cs` — `[Verb("get-min-darc-version", ...)] : PcsApiOptions`.
   - `SetMinDarcVersionOptions.cs` — `[Verb("set-min-darc-version", ...)] : PcsApiOptions`. `[Value(0, MetaName = "version", Required = true)] public string Version { get; set; }`.
   - `ClearMinDarcVersionOptions.cs` — `[Verb("clear-min-darc-version", ...)] : PcsApiOptions`.
2. New files under `tools/ProductConstructionService.Cli/Operations/`:
   - `GetMinDarcVersionOperation.cs`, `SetMinDarcVersionOperation.cs`, `ClearMinDarcVersionOperation.cs`.
   - Each takes `IProductConstructionServiceApi` + `ILogger<T>`.
   - `Set` operation passes `Version` from options through to the API call.
3. Wire verbs into `tools/ProductConstructionService.Cli/Program.cs` `Parser.Default.ParseArguments<...>()` type list.
4. The actual API client method names will land after the user regenerates the client; stub the call sites against the expected method names (e.g. `_client.ClientVersion.GetMinDarcAsync()` etc.) — comment a note that names depend on regen.

---

## Phase 4 — Controller unit tests

**Goal:** unit-test `ClientVersionController` end-to-end (mock `IRedisCacheFactory` + `IRedisCache`).

**Implementation:**
1. New file `test/ProductConstructionService/ProductConstructionService.Api.Tests/ClientVersionControllerTests.cs` mirroring the `FeatureFlagsController20200220Tests` pattern (NUnit `[TestFixture]`, `[SetUp]`, Moq, AwesomeAssertions).
2. Cases:
   - `GET` returns `Ok` with the version when Redis returns a value.
   - `GET` returns `NoContent` when Redis returns null.
   - `PUT` with valid `NuGetVersion` calls `SetAsync` once and returns `Ok`.
   - `PUT` with invalid version string returns `BadRequest` and does NOT call `SetAsync`.
   - `DELETE` calls `TryDeleteAsync` and returns `NoContent` regardless of return value.

**No middleware unit tests, no CLI tests** (per user direction).

---

## Relevant files

- New: `src/ProductConstructionService/ProductConstructionService.Api/Configuration/ClientVersionEnforcementMiddleware.cs`
- New: `src/ProductConstructionService/ProductConstructionService.Api/Configuration/MinClientVersionConstants.cs`
- Modify: `src/ProductConstructionService/ProductConstructionService.Api/Program.cs` — register middleware before `UseAuthentication()` at line 65.
- New: `src/ProductConstructionService/ProductConstructionService.Api/Controllers/ClientVersionController.cs`
- New: `src/ProductConstructionService/ProductConstructionService.Api/Api/MinClientVersionRequest.cs` (and response DTO)
- New: `tools/ProductConstructionService.Cli/Options/{Get,Set,Clear}MinDarcVersionOptions.cs`
- New: `tools/ProductConstructionService.Cli/Operations/{Get,Set,Clear}MinDarcVersionOperation.cs`
- Modify: `tools/ProductConstructionService.Cli/Program.cs` — add new verb types to `ParseArguments`.
- New: `test/ProductConstructionService/ProductConstructionService.Api.Tests/ClientVersionControllerTests.cs`
- Reuse: `Maestro.Services.Common.Cache.IRedisCacheFactory`, `ProductConstructionService.Api.Api.ApiError`, `AuthenticationConfiguration.AdminAuthorizationPolicyName`, `NuGet.Versioning.NuGetVersion`.

---

## Verification

1. `./build.sh` succeeds with no new warnings.
2. `./.dotnet/dotnet test --no-build --filter "FullyQualifiedName~ClientVersionControllerTests"` passes all 5+ cases.
3. Manual: launch PCS via Aspire, hit a `/api/*` route with no headers → expect 200 (pass-through). Hit with `X-Client-Name=darc` + `X-Client-Version=0.0.99-dev` → 200. With version below configured min → 426 + `X-Minimum-Client-Version` header. With no Redis key set → 200 and a warning in logs.
4. Manual: run pcs-cli `set-min-darc-version 1.2.3`, then `get-min-darc-version`, then `clear-min-darc-version`. Verify Redis state via direct inspection.

---

## Decisions captured

- Dev-version bypass: any `X-Client-Version` ending in `-dev` (case-insensitive). Bypass occurs after header presence check and after client-name filter.
- Redis key shape: single global `min-client-version-darc`, no TTL. Missing key ⇒ pass-through with warning log. Lookup error ⇒ pass-through with warning (fail-open).
- Enforcement runs as ASP.NET middleware **before** `UseAuthentication()` (so even unauthenticated old clients get a clean 426). Scoped to `/api/*` only — health/swagger/`status` GET unaffected.
- Missing headers ⇒ pass-through (no enforcement when client identity is unknown; avoids breaking non-darc / older callers that don't send the headers).
- Version comparison via `NuGet.Versioning.NuGetVersion`. Unparseable client version ⇒ 426. Unparseable Redis value ⇒ pass-through + error log.
- Per-case `ApiError.Message` text (server owns wording; client prints verbatim per client-side Phase 4).
- New `ClientVersionController` (separate from `StatusController`), admin-only via `AdminAuthorizationPolicyName`. GET / PUT / DELETE for `/client-version/min-darc`.
- `IRedisCacheFactory` reused directly (no new dedicated service abstraction). Key constant centralized in `MinClientVersionConstants`.
- pcs-cli: three flat verbs (`get-min-darc-version`, `set-min-darc-version <version>`, `clear-min-darc-version`). No interactive confirmation.
- Tests: only controller unit tests (Phase 4). No middleware unit tests, no CLI tests, no integration tests.
- Generated client code is NOT touched in this plan — user will regenerate after merging server changes.

## Rollout note

Critical ordering reminder from client-side plan: ship the new Client lib (with always-on headers + 426 translation) and have darc consumers adopt it BEFORE the server min-version key is set in Redis. Until the key is set, the middleware passes through with a warning, so deploying the API changes alone is safe — enforcement only "turns on" when an admin runs `set-min-darc-version`.
