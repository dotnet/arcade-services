// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Net;
using System.Reflection;
using Maestro.Common;
using Maestro.Services.Common.Cache;
using Microsoft.AspNetCore.ApiVersioning;
using Microsoft.AspNetCore.ApiVersioning.Swashbuckle;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.DotNet.DarcLib;
using NuGet.Versioning;
using ProductConstructionService.Api.Api;
using ProductConstructionService.Api.Configuration;

namespace ProductConstructionService.Api.Controllers;

/// <summary>
/// Provides darc client version information. Admins can manage the minimum required
/// darc client version, and any client can fetch the currently active darc version
/// running in this deployment, or the configured minimum version.
/// </summary>
[Route("darc-version")]
[ApiVersion("2020-02-20")]
[Authorize(Policy = AuthenticationConfiguration.AdminAuthorizationPolicyName)]
public class DarcVersionController : ControllerBase
{
    private readonly IRedisCacheFactory _redisCacheFactory;

    public DarcVersionController(IRedisCacheFactory redisCacheFactory)
    {
        _redisCacheFactory = redisCacheFactory;
    }

    [HttpGet(Name = "GetDarcVersion")]
    [AllowAnonymous]
    [SwaggerApiResponse(HttpStatusCode.OK, Type = typeof(string), Description = "Gets the version of darc in use by this PCS instance.")]
    public IActionResult GetDarcVersion()
    {
        // Use the assembly file version, which is the same as the package
        // version. The informational version has a "+<sha>" appended to the end for official builds
        // We don't want this, so eliminate it. The primary use of this is to install the darc version
        // corresponding to the PCS version.
        AssemblyInformationalVersionAttribute? informationalVersionAttribute =
            typeof(IRemote).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
        var version = informationalVersionAttribute?.InformationalVersion!;
        var lastPlus = version.LastIndexOf('+');
        if (lastPlus != -1)
        {
            version = version.Substring(0, lastPlus);
        }
        return Ok(version);
    }

    [HttpGet("min", Name = "GetMinDarcVersion")]
    [AllowAnonymous]
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

    [HttpPut("min", Name = "SetMinDarcVersion")]
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

    [HttpDelete("min", Name = "ClearMinDarcVersion")]
    [SwaggerApiResponse(HttpStatusCode.NoContent, Description = "Minimum darc client version cleared")]
    public async Task<IActionResult> ClearMinDarcVersionAsync()
    {
        var cache = _redisCacheFactory.Create(MinClientVersionConstants.DarcMinVersionRedisKey);
        await cache.TryDeleteAsync();

        return NoContent();
    }
}
