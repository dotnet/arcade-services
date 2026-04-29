// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Net;
using Maestro.Common;
using Maestro.Services.Common.Cache;
using Microsoft.AspNetCore.ApiVersioning;
using Microsoft.AspNetCore.ApiVersioning.Swashbuckle;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NuGet.Versioning;
using ProductConstructionService.Api.Api;
using ProductConstructionService.Api.Configuration;

namespace ProductConstructionService.Api.Controllers;

/// <summary>
/// Admin-only management of the minimum required darc client version enforced by
/// <see cref="ClientVersionEnforcementMiddleware"/>.
/// </summary>
[Route("min-darc-version")]
[ApiVersion("2020-02-20")]
[Authorize(Policy = AuthenticationConfiguration.AdminAuthorizationPolicyName)]
public class MinDarcVersionController : ControllerBase
{
    private readonly IRedisCacheFactory _redisCacheFactory;

    public MinDarcVersionController(IRedisCacheFactory redisCacheFactory)
    {
        _redisCacheFactory = redisCacheFactory;
    }

    [HttpGet(Name = "GetMinDarcVersion")]
    [SwaggerApiResponse(HttpStatusCode.OK, Type = typeof(string), Description = "Returns the configured minimum darc client version")]
    [SwaggerApiResponse(HttpStatusCode.NoContent, Description = "No minimum darc client version is configured")]
    public async Task<IActionResult> GetMinDarcVersionAsync()
    {
        var cache = _redisCacheFactory.Create(MinClientVersionConstants.DarcMinVersionRedisKey);
        var value = await cache.TryGetAsync();

        if (string.IsNullOrWhiteSpace(value))
        {
            return NoContent();
        }

        return Ok(value);
    }

    [HttpPut(Name = "SetMinDarcVersion")]
    [SwaggerApiResponse(HttpStatusCode.OK, Description = "Minimum darc client version stored")]
    [SwaggerApiResponse(HttpStatusCode.BadRequest, Type = typeof(ApiError), Description = "The supplied version could not be parsed")]
    public async Task<IActionResult> SetMinDarcVersionAsync([FromQuery] string minimumVersion)
    {
        if (string.IsNullOrWhiteSpace(minimumVersion))
        {
            return BadRequest(new ApiError("A minimum version must be supplied."));
        }

        if (!NuGetVersion.TryParse(minimumVersion, out _))
        {
            return BadRequest(new ApiError($"Invalid version: '{minimumVersion}'."));
        }

        var cache = _redisCacheFactory.Create(MinClientVersionConstants.DarcMinVersionRedisKey);
        await cache.SetAsync(minimumVersion);

        return Ok();
    }

    [HttpDelete(Name = "ClearMinDarcVersion")]
    [SwaggerApiResponse(HttpStatusCode.NoContent, Description = "Minimum darc client version cleared")]
    public async Task<IActionResult> ClearMinDarcVersionAsync()
    {
        var cache = _redisCacheFactory.Create(MinClientVersionConstants.DarcMinVersionRedisKey);
        await cache.TryDeleteAsync();

        return NoContent();
    }
}
