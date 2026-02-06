// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.ComponentModel.DataAnnotations;
using System.Net;
using Microsoft.AspNetCore.ApiVersioning;
using Microsoft.AspNetCore.ApiVersioning.Swashbuckle;
using Microsoft.AspNetCore.Mvc;
using Microsoft.DotNet.DarcLib;
using ProductConstructionService.Api.v2019_01_16.Models;

namespace ProductConstructionService.Api.Api.v2019_01_16.Controllers;

/// <summary>
///   Exposes methods to Read <see cref="BuildTime"/>s
/// </summary>
[Route("buildtime")]
[ApiVersion("2019-01-16")]
public class BuildTimeController : ControllerBase
{
    private readonly IBasicBarClient _barClient;

    public BuildTimeController(IBasicBarClient barClient)
    {
        _barClient = barClient;
    }

    /// <summary>
    /// Gets the average official build time and average pr build time for a given default channel
    /// This is captured for generating the longest build times for the dependency flow graph
    /// </summary>
    /// <param name="id">Default channel id</param>
    /// <param name="days">Number of days to summarize over</param>
    [HttpGet("{id}")]
    [SwaggerApiResponse(HttpStatusCode.OK, Type = typeof(BuildTime), Description = "Gets the average official build time and average pr build time for a given default channel")]
    [ValidateModelState]
    public virtual async Task<IActionResult> GetBuildTimes([Required] int id, int days = 7)
    {
        var buildTime = await _barClient.GetBuildTimeAsync(id, days);
        if (buildTime == null)
        {
            return NotFound();
        }

        return Ok(new BuildTime(
            buildTime.DefaultChannelId ?? 0,
            buildTime.OfficialBuildTime ?? 0,
            buildTime.PrBuildTime ?? 0,
            buildTime.GoalTimeInMinutes ?? 0));
    }
}
