# Plan: PCS minimum darc-version enforcement — Client side

The PCS API will be able to reject requests from darc CLIs older than a configured minimum. This plan covers ONLY the client side (the DARC CLI + the shared `Microsoft.DotNet.ProductConstructionService.Client` library). The server/API side will be planned separately.

Wire contract assumed (will be finalized in API-side plan):
- Client → server request headers: `X-Client-Name`, `X-Client-Version`
- Server → client rejection: HTTP `426 Upgrade Required`
  - Response header `X-Minimum-Client-Version: <semver>` (structured)
  - Response body: `ApiError` whose `Message` carries a human-readable message (server-authored, optional)

---

## Phase 1 — Always-on client identity headers in the Client library

Goal: every request from any consumer of the Client library carries `X-Client-Name` + `X-Client-Version` headers. The server can then require headers (catching old darc binaries that don't send them) AND read `X-Client-Name == "darc"` to scope the minimum-version rule to darc only.

Key design points:
- **Always-on, no opt-out.** The Client library unconditionally attaches an `HttpPipelinePolicy` that sets both headers on every request. Old darc binaries (running the *old* Client lib) won't send the headers and will be denied by the server — that's the intended behavior.
- **Sensible default for `clientName`/`clientVersion`.** Library reads `Assembly.GetEntryAssembly()` for default name (e.g. `Microsoft.DotNet.Darc`) and `FileVersionInfo.ProductVersion` for default version. Falls back to literal `"unknown"` sentinels if detection fails — never omit the headers, never send empty values.
- **Override available, not required.** Factory methods get optional `clientName`/`clientVersion` parameters; supply them when the caller wants a stable identity that doesn't depend on the entry-assembly name (darc does this).
- **Old non-darc consumers get blocked once.** They have to upgrade their reference to the new Client lib so it sends headers. This is the accepted one-time pain.
- **Server logic** (planned in API-side phase, summarized here for context): require headers present → otherwise `426`. Then enforce minimum version *only* when `X-Client-Name == "darc"`. Other names (and `unknown`) bypass the version check.

Implementation steps:
1. Extend `PcsApiFactory.GetAuthenticated(...)` and `GetAnonymous(...)` (and the `BarApiClient` overloads) with optional `string? clientName = null` / `string? clientVersion = null` parameters. Defaults stay backward-compatible.
2. In `ProductConstructionServiceApiOptions.InitializeOptions()` (where `BearerTokenAuthenticationPolicy` is added), unconditionally register a new request-header `HttpPipelinePolicy`. Inside the policy, if the caller supplied overrides use them; otherwise fall back to entry-assembly inspection; otherwise `"unknown"`.
3. In darc, pass `clientName: "darc"` to `PcsApiFactory` (via `BarApiClient`). Let `clientVersion` default to entry-assembly version (which is darc's own version when darc is the entry assembly). Plumb the override through DI so `BarApiClient` reads it nullable and forwards.
4. The override resolution happens at factory call time (or first-use), not pipeline-build time, so per-call diagnostics work.

---

## Phase 2 — Translate 426 to a typed exception in the Client library

Goal: when the server rejects with 426, the Client library throws a dedicated, non-`RestApiException` exception so darc can surface a friendly message and so per-operation `catch (RestApiException ...)` filters can't swallow it.

Steps:
1. Add a new public exception type `ClientVersionTooOldException : Exception` in the Client library, carrying:
   - `string? ClientName` (what we sent in `X-Client-Name`, e.g. `"darc"`)
   - `string? CurrentVersion` (what we sent in `X-Client-Version`)
   - `string? MinimumVersion` (from `X-Minimum-Client-Version` response header, if present)
   - `string? ServerMessage` (from `ApiError.Message` body, if any)
   - Inner exception = the original `RestApiException`.
   - Name reflects that this also covers "headers missing" rejections from non-darc consumers, not only darc-version-too-old.
2. Add a hand-written partial file `ProductConstructionServiceApi.HandleFailedRequest.cs` next to the existing hand-written partials (`Build.cs`, `Subscription.cs`, etc.). Implement the partial method `HandleFailedRequest(RestApiException ex)` declared at line 199 of [Generated/ProductConstructionServiceApi.cs](../src/ProductConstructionService/Microsoft.DotNet.ProductConstructionService.Client/Generated/ProductConstructionServiceApi.cs#L199).
   - When `ex.Response.Status == 426`, read the minimum-version header and (if present) deserialize `ApiError.Message`, then `throw new ClientVersionTooOldException(...)`. This pre-empts the `throw ex;` line in every generated `On<Op>Failed` method (see [Generated/Assets.cs](../src/ProductConstructionService/Microsoft.DotNet.ProductConstructionService.Client/Generated/Assets.cs#L139-L148)).
3. The current client name/version are the values we send in headers, captured at factory-construction time so the partial can include them in the exception. Plumb them through `ProductConstructionServiceApiOptions` so the partial has access.

---

## Phase 3 — Make `ClientVersionTooOldException` bubble past per-operation `catch (Exception)` blocks

Goal: ensure the typed exception reaches `Program.RunOperation` no matter which operation triggered it. Today most darc operations have a `catch (Exception e)` that flattens any error into "an error occurred".

Steps:
1. Add a small extension/helper `ExceptionExtensions.RethrowIfFatal(this Exception e)` in darc (or DarcLib) that rethrows when `e is ClientVersionTooOldException` (extensible to other future "must reach top" exceptions).
2. Sweep all `catch (Exception ...)` blocks in `src/Microsoft.DotNet.Darc/Darc/Operations/**`. There are ~20+ such blocks ([VmrOperationBase.cs#L111](../src/Microsoft.DotNet.Darc/Darc/Operations/VirtualMonoRepo/VmrOperationBase.cs#L111), `AddBuildToChannelOperation.cs`, `GatherDropOperation.cs`, etc.). Each gets `e.RethrowIfFatal();` as the first line of the catch body.
3. Add a comment / contributor-doc note in the helper explaining why every `catch (Exception)` in operations must call it.
4. Optional hardening: an architecture/analyzer test (Roslyn-based or simple text scan) that fails CI when a new `catch (Exception` appears in `Operations/` without a `RethrowIfFatal` call. Skip for v1 if it's too much work.

---

## Phase 4 — Friendly handling in darc top-level (`Program.RunOperation`)

Goal: a single place produces the upgrade message and exit code.

Steps:
1. In [src/Microsoft.DotNet.Darc/Darc/Program.cs](../src/Microsoft.DotNet.Darc/Darc/Program.cs#L88) `RunOperation`, add a `catch (ClientVersionTooOldException ex)` *before* the existing `catch (Exception e)`.
2. Build the message:
   - If `ex.ServerMessage` is non-empty → print verbatim (server can override messaging without redeploying darc, and can tailor wording per `X-Client-Name`).
   - Else → fallback: `"Your darc version {ex.CurrentVersion ?? <FileVersionInfo>} is below the minimum required ({ex.MinimumVersion ?? "unknown"}). Run `darc-init` (or `dotnet tool update -g microsoft.dotnet.darc`) to upgrade."`.
3. Add a dedicated exit code (e.g., `Constants.VersionMismatchErrorCode`) so scripts can disambiguate.
4. Print to `Console.Error`; do not log via `ILogger` (consistent with the existing `Console.WriteLine("Unhandled exception encountered")` block).

---

## Relevant files

- [src/ProductConstructionService/Microsoft.DotNet.ProductConstructionService.Client/PcsApiFactory.cs](../src/ProductConstructionService/Microsoft.DotNet.ProductConstructionService.Client/PcsApiFactory.cs) — add optional `clientName` / `clientVersion` params to `GetAuthenticated`/`GetAnonymous`.
- [src/ProductConstructionService/Microsoft.DotNet.ProductConstructionService.Client/ProductConstructionServiceApiOptions.cs](../src/ProductConstructionService/Microsoft.DotNet.ProductConstructionService.Client/ProductConstructionServiceApiOptions.cs) — unconditionally register a request-header `HttpPipelinePolicy` in `InitializeOptions()`; resolve defaults from entry assembly with `"unknown"` fallback.
- New file `ProductConstructionServiceApi.HandleFailedRequest.cs` (Client library) — hand-written partial implementing 426 → `ClientVersionTooOldException` translation. Pattern matches existing partials `Build.cs`, `Subscription.cs`.
- New file `ClientVersionTooOldException.cs` (Client library) — public exception type.
- [src/Microsoft.DotNet.Darc/DarcLib/BarApiClient.cs](../src/Microsoft.DotNet.Darc/DarcLib/BarApiClient.cs) — accept and forward `clientName`/`clientVersion` overrides to factory.
- [src/Microsoft.DotNet.Darc/Darc/Program.cs](../src/Microsoft.DotNet.Darc/Darc/Program.cs) — provide identity (`"darc"` + entry-assembly version) to `BarApiClient` via DI; add `catch (ClientVersionTooOldException)` in `RunOperation`.
- [src/Microsoft.DotNet.Darc/Darc/Operations/**](../src/Microsoft.DotNet.Darc/Darc/Operations/) — sweep `catch (Exception)` to call `RethrowIfFatal()`. ~20+ files including `VmrOperationBase.cs`, `AddBuildToChannelOperation.cs`, `GatherDropOperation.cs`, `AddSubscriptionOperation.cs`, etc.
- New helper `ExceptionExtensions.cs` (darc or DarcLib) — `RethrowIfFatal` extension.

---

## Decisions captured

- Header names: `X-Client-Name`, `X-Client-Version` (request); `X-Minimum-Client-Version` (response).
- Two-part identity over a single `X-Darc-Version` so the server can require headers from everyone but only enforce a *version minimum* against `X-Client-Name == "darc"`. Other consumers (and `unknown`) bypass the minimum check.
- **Always-on headers** in the Client library — no opt-out. Defaults from entry-assembly `Name` / `FileVersionInfo.ProductVersion`; fall back to literal `"unknown"` rather than omit/empty. Optional `clientName`/`clientVersion` overrides on factory; darc supplies `"darc"` for stability.
- Rejection status: `426 Upgrade Required` (server-side decision; revisit in API-side plan). Used both for "headers missing" (catches old clients) and "darc version below minimum".
- Error delivery: hybrid — structured `X-Minimum-Client-Version` header + optional server-authored `ApiError.Message` body; client uses server message verbatim if present, else builds a darc-flavored fallback.
- Translate 426 in the Client library via the generator's `HandleFailedRequest` partial; new exception (`ClientVersionTooOldException`) inherits from `Exception`, NOT `RestApiException`, so existing per-op `RestApiException` filters don't intercept it.
- Per-op `catch (Exception)` sweep is required because `RestApiException`-only filters are not the only swallow points (cf. [VmrOperationBase.cs#L111](../src/Microsoft.DotNet.Darc/Darc/Operations/VirtualMonoRepo/VmrOperationBase.cs#L111)).
- Exception name is `ClientVersionTooOldException`, not `DarcVersionTooOldException`, because the same exception fires for non-darc consumers using an old Client lib that doesn't send headers yet.
- Non-darc consumers: blocked once when server enforcement turns on, until they update to a Client lib version that sends headers. Acceptable one-time pain.

## Open items for team discussion

1. **Rollout ordering is critical.** Ship the new Client lib (always-on headers + Phase 2/3/4 translation/handler) and have darc + other consumers adopt it BEFORE the server enables enforcement. Otherwise enforcement day breaks every consumer simultaneously without a friendly message. Coordinate the cutover.
2. **Optional CI guard** for "every `catch (Exception)` in `Operations/` must call `RethrowIfFatal`". Punt to v2 unless we feel burned.
3. **Exact exit code value** for `VersionMismatchErrorCode` — pick something not colliding with current `Constants.ErrorCode`.
4. **Sentinel value for unknown identity.** Confirm `"unknown"` is fine, or pick something less collision-prone (e.g. `"unidentified-client"`).
