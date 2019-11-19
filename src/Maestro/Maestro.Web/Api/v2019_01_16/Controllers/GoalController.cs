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
        ///   Exposes methods to set goal time <see cref="Goal"/>
        /// </summary>
        /// <param name="ChannelName"></param>
        /// <param name="DefinitionId"></param>
        /// <param name="GoalInMinutes"></param>
        [HttpPost("{DefinitionId}/{ChannelName}")]
        [SwaggerApiResponse(System.Net.HttpStatusCode.Created, Type = typeof(Models.Goal), Description = "Goal for given Channel and Definition-Id is created/updated")]
        [ValidateModelState]
        public virtual async Task<IActionResult> Create([Required] int DefinitionId, [Required] string ChannelName, [Required] int GoalInMinutes)
        {
            Data.Models.Channel channel = await _context.Channels
                    .Where(c => c.Name.Equals(ChannelName)).FirstOrDefaultAsync();
            if (channel == null)
            {
                return NotFound();
            }

            Data.Models.GoalTime goal = await _context.GoalTime
                .Where(g => g.DefinitionId == DefinitionId && g.ChannelId == channel.Id).FirstOrDefaultAsync();
           
            if (goal == null)
            {
                goal = new Data.Models.GoalTime
                {
                    DefinitionId = DefinitionId,
                    Minutes = GoalInMinutes,
                    ChannelId = channel.Id
                };
                await _context.GoalTime.AddAsync(goal);
            }
            else
            // If the combination of Channel and DefinitionId already exists then update the exisiting record
            {
                goal.Minutes = GoalInMinutes;
                _context.GoalTime.Update(goal);  
            }
            await _context.SaveChangesAsync();
            return Ok(new Goal(goal));
        }

        /// <summary>
        /// Get goal time in minutes <see cref="Goal"/>s that matches ChannelName and DefinitionId
        /// </summary>
        /// <param name="ChannelName"></param>
        /// <param name="DefinitionId"></param>
        [HttpGet("{DefinitionId}/{ChannelName}")]
        [SwaggerApiResponse(HttpStatusCode.OK, Type = typeof(Goal), Description = "Get the Goal for a given Channel and Definition-Id")]
        [ValidateModelState]
        public virtual async Task<IActionResult> GetGoalTimes([Required]int DefinitionId, [Required]string ChannelName)
        {
            Data.Models.Channel channel = await _context.Channels
                    .Where(c => c.Name.Equals(ChannelName)).FirstOrDefaultAsync();
            if (channel == null)
            {
                return NotFound();
            }

            Data.Models.GoalTime goal = await _context.GoalTime
            .Where(g => g.DefinitionId == DefinitionId && g.ChannelId == channel.Id).FirstOrDefaultAsync();
            
            if (goal == null)
            {
                return NotFound();
            }
            return Ok(goal); ;
        }
    }
}
