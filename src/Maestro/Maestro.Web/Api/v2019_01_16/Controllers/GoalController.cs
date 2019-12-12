// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
using Maestro.Data;
using Maestro.Data.Models;
using Maestro.Web.Api.v2019_01_16.Models;
using Microsoft.AspNetCore.ApiVersioning;
using Microsoft.AspNetCore.ApiVersioning.Swashbuckle;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.KeyVault;
using Microsoft.EntityFrameworkCore;
using System.Net;

namespace Maestro.Web.Api.v2019_01_16.Controllers
{
    [Route("goals")]
    [ApiVersion("2019-01-16")]
    public class GoalController : ControllerBase
    {
        protected readonly BuildAssetRegistryContext _context;
        public GoalController(BuildAssetRegistryContext context)
        {
            _context = context;
        }

        /// <summary>
        /// Sets a build time in minutes <see cref="Goal"/> for a given Definition in a Channel.
        /// This is captured for the Power BI Dashboard -Internal Report under .Net Core Engineering Services workspace.
        /// </summary>
        /// <param name="goalData">An object containing build time goal in minutes <see cref="Goal"/></param>
        /// <param name="channelName">Channel Name for the build time Eg. .Net Core 5</param>
        /// <param name="definitionId">Azure DevOps pipeline Definition Id</param>
        [HttpPut("channelName/{channelName}/definitionId/{definitionId}")]
        [SwaggerApiResponse(System.Net.HttpStatusCode.OK, Type = typeof(Models.Goal), Description = "Sets a build time goal (in minutes) for a given Definition in a Channel.")]
        [ValidateModelState]
        public virtual async Task<IActionResult> Create([FromBody, Required] Goal.GoalRequestJson goalData,[Required] String channelName , [Required] int definitionId)
        {
            Data.Models.Channel channel = await _context.Channels
                .FirstOrDefaultAsync(c => c.Name.Equals(channelName));
            if (channel == null)
            {
                return NotFound();
            }
            Data.Models.GoalTime goal = await _context.GoalTime
                .FirstOrDefaultAsync(g => g.DefinitionId == definitionId && g.ChannelId == channel.Id);
            if (goal == null)
            {
                goal = new Data.Models.GoalTime
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
        public virtual async Task<IActionResult> GetGoalTimes([Required]int definitionId, [Required]string channelName)
        {
            Data.Models.Channel channel = await _context.Channels
                .FirstOrDefaultAsync(c => c.Name.Equals(channelName));
            if (channel == null)
            {
                return NotFound();
            }
            Data.Models.GoalTime goal = await _context.GoalTime
                .FirstOrDefaultAsync(g => g.DefinitionId == definitionId && g.ChannelId == channel.Id);
            if (goal == null)
            {
                return NotFound();
            }
            return Ok(new Goal(goal));
        }
    }
}
