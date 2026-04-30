// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text.Json;
using Maestro.Common;
using Maestro.Services.Common.Cache;
using NuGet.Versioning;
using ProductConstructionService.Api.Api;

namespace ProductConstructionService.Api.Configuration;

/// <summary>
/// Rejects /api/* requests from darc clients older than the configured minimum version
/// with HTTP 426 Upgrade Required. Runs before authentication so even unauthenticated
/// old clients get a clean 426.
/// </summary>
public class ClientVersionEnforcementMiddleware : IMiddleware
{
    private const string ApiPathSegment = "/api";
    private const string DevVersionSuffix = "-dev";

    private readonly IRedisCacheFactory _redisCacheFactory;
    private readonly ILogger<ClientVersionEnforcementMiddleware> _logger;

    public ClientVersionEnforcementMiddleware(
        IRedisCacheFactory redisCacheFactory,
        ILogger<ClientVersionEnforcementMiddleware> logger)
    {
        _redisCacheFactory = redisCacheFactory;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        // 1. Headers missing -> pass through (no enforcement when client identity is unknown).
        if (!context.Request.Headers.TryGetValue(MinClientVersionConstants.ClientNameHeader, out var clientNameValues)
            || !context.Request.Headers.TryGetValue(MinClientVersionConstants.ClientVersionHeader, out var clientVersionValues))
        {
            await next(context);
            return;
        }

        var clientName = clientNameValues.ToString();
        var clientVersionString = clientVersionValues.ToString();

        if (string.IsNullOrWhiteSpace(clientName) || string.IsNullOrWhiteSpace(clientVersionString))
        {
            await next(context);
            return;
        }

        // 2. Only darc is enforced.
        if (!string.Equals(clientName, MinClientVersionConstants.DarcClientName, StringComparison.OrdinalIgnoreCase))
        {
            await next(context);
            return;
        }

        // 3. Dev builds bypass enforcement.
        if (clientVersionString.EndsWith(DevVersionSuffix, StringComparison.OrdinalIgnoreCase))
        {
            await next(context);
            return;
        }

        // 4. Read minimum from Redis.
        string? minVersionString;
        try
        {
            var cache = _redisCacheFactory.Create(MinClientVersionConstants.DarcMinVersionRedisKey);
            minVersionString = await cache.TryGetAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read minimum darc client version from Redis; passing request through.");
            await next(context);
            return;
        }

        if (string.IsNullOrWhiteSpace(minVersionString))
        {
            _logger.LogWarning("Minimum client version not configured in Redis (key '{Key}'); passing request through.",
                MinClientVersionConstants.DarcMinVersionRedisKey);
            await next(context);
            return;
        }

        // 5. Parse client version.
        if (!NuGetVersion.TryParse(clientVersionString, out var clientVersion))
        {
            await WriteRejectionAsync(
                context,
                minimumVersionHeaderValue: null,
                message: $"Client version '{clientVersionString}' could not be parsed. Please upgrade your darc client.");
            return;
        }

        // 6. Parse minimum version. Bad config -> log and pass through.
        if (!NuGetVersion.TryParse(minVersionString, out var minVersion))
        {
            _logger.LogError(
                "Minimum darc client version stored in Redis (key '{Key}') is not a valid version: '{Value}'. Passing request through.",
                MinClientVersionConstants.DarcMinVersionRedisKey,
                minVersionString);
            await next(context);
            return;
        }

        // 7. Compare.
        if (clientVersion < minVersion)
        {
            await WriteRejectionAsync(
                context,
                minimumVersionHeaderValue: minVersion.ToNormalizedString(),
                message: $"Your darc version {clientVersion.ToNormalizedString()} is below the minimum required version {minVersion.ToNormalizedString()}. Run `darc-init` (or `dotnet tool update -g microsoft.dotnet.darc`) to upgrade.");
            return;
        }

        await next(context);
    }

    private static async Task WriteRejectionAsync(HttpContext context, string? minimumVersionHeaderValue, string message)
    {
        context.Response.StatusCode = StatusCodes.Status426UpgradeRequired;
        context.Response.ContentType = "application/json";

        if (!string.IsNullOrEmpty(minimumVersionHeaderValue))
        {
            context.Response.Headers[MinClientVersionConstants.MinimumClientVersionHeader] = minimumVersionHeaderValue;
        }

        var payload = JsonSerializer.Serialize(
            new ApiError(message));

        await context.Response.WriteAsync(payload);
    }
}
