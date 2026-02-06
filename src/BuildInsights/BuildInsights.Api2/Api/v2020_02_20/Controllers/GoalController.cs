// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.ComponentModel.DataAnnotations;
using System.Net;
using Maestro.Data;
using Microsoft.AspNetCore.ApiVersioning;
using Microsoft.AspNetCore.ApiVersioning.Swashbuckle;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProductConstructionService.Api.v2020_02_20.Models;

namespace ProductConstructionService.Api.Api.v2020_02_20.Controllers;

[Route("goals")]
[ApiVersion("2020-02-20")]
public class GoalController : v2019_01_16.Controllers.GoalController
{
    public GoalController(BuildAssetRegistryContext context)
        : base(context)
    {
    }

    [ApiRemoved]
    public override Task<IActionResult> Create([FromBody, Required] ProductConstructionService.Api.v2019_01_16.Models.Goal.GoalRequestJson goalData, [Required] string channelName, [Required] int definitionId)
    {
        throw new NotImplementedException();
    }

    /// <summary>
    /// Sets a build time in minutes <see cref="Goal"/> for a given Definition in a Channel.
    /// This is captured for the Power BI Dashboard -Internal Report under .Net Core Engineering Services workspace.
    /// </summary>
    /// <param name="goalData">An object containing build time goal in minutes <see cref="Goal"/></param>
    /// <param name="channelName">Channel Name for the build time Eg. .Net Core 5</param>
    /// <param name="definitionId">Azure DevOps pipeline Definition Id</param>
    [HttpPut("channelName/{channelName}/definitionId/{definitionId}")]
    [SwaggerApiResponse(HttpStatusCode.OK, Type = typeof(Goal), Description = "Sets a build time goal (in minutes) for a given Definition in a Channel.")]
    [ValidateModelState]
    public virtual async Task<IActionResult> Create([FromBody, Required] Goal.GoalRequestJson goalData, [Required] string channelName, [Required] int definitionId)
    {
        Maestro.Data.Models.Channel? channel = await _context.Channels
            .FirstOrDefaultAsync(c => c.Name == channelName);
        if (channel == null)
        {
            return NotFound();
        }
        Maestro.Data.Models.GoalTime? goal = await _context.GoalTime
            .FirstOrDefaultAsync(g => g.DefinitionId == definitionId && g.ChannelId == channel.Id);
        if (goal == null)
        {
            goal = new Maestro.Data.Models.GoalTime
            {
                DefinitionId = definitionId,
                Minutes = goalData.Minutes,
                ChannelId = channel.Id
            };
            await _context.GoalTime.AddAsync(goal);
        }
        else
        {
            goal.Minutes = goalData.Minutes;
            _context.GoalTime.Update(goal);
        }
        await _context.SaveChangesAsync();
        return Ok(new Goal(goal));
    }

    /// <summary>
    /// Gets the build time in minutes <see cref="Goal"/> for a given Definition in a Channel.
    /// This is captured for the Power BI Dashboard -Internal Report under .Net Core Engineering Services workspace.
    /// </summary>
    /// <param name="channelName">Channel Name for the build time Eg. .Net Core 5</param>
    /// <param name="definitionId">Azure DevOps pipeline Definition Id</param>
    [HttpGet("channelName/{channelName}/definitionId/{definitionId}")]
    [SwaggerApiResponse(HttpStatusCode.OK, Type = typeof(Goal), Description = "Gets the build time goal (in minutes) for a given Definition in a Channel.")]
    [ValidateModelState]
    public override async Task<IActionResult> GetGoalTimes([Required] int definitionId, [Required] string channelName)
    {
        Maestro.Data.Models.Channel? channel = await _context.Channels
            .FirstOrDefaultAsync(c => c.Name == channelName);
        if (channel == null)
        {
            return NotFound();
        }
        Maestro.Data.Models.GoalTime? goal = await _context.GoalTime
            .FirstOrDefaultAsync(g => g.DefinitionId == definitionId && g.ChannelId == channel.Id);
        if (goal == null)
        {
            return NotFound();
        }
        return Ok(new Goal(goal));
    }
}
