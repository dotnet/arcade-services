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
        ///   Exposes methods to Read/Query <see cref="Goal"/>
        /// </summary>
        /// <param name="ChannelName"></param>
        /// <param name="DefinitionId"></param>
        /// <param name="Minutes"></param>
        [HttpPost]
        [SwaggerApiResponse(System.Net.HttpStatusCode.Created, Type = typeof(Models.Goal), Description = "The created goaltime is :")]
        [ValidateModelState]
        public virtual async Task<IActionResult> Create([Required] int DefinitionId, [Required] string ChannelName, [Required] int Minutes)
        {

            Data.Models.Channel channel = await _context.Channels
                    .Where(c => c.Name.Equals(ChannelName)).FirstOrDefaultAsync();
            Data.Models.GoalTime goal = await _context.GoalTime
                .Where(g => g.DefinitionId == DefinitionId && g.ChannelId == channel.Id).FirstOrDefaultAsync();

            if (channel == null)
            {
                return NotFound();
            }
            if (goal == null)
            {
                var goalModel = new Data.Models.GoalTime
                {
                    DefinitionId = DefinitionId,
                    Minutes = Minutes,
                    ChannelId = channel.Id
                };
                await _context.GoalTime.AddAsync(goalModel);
                await _context.SaveChangesAsync();
                var goalTest = CreatedAtRoute(
                    new
                    {
                        action = "CreateGoal",
                        DefinitionId = DefinitionId,
                        Minutes = Minutes,
                        ChannelId = channel.Id
                    },
                    new Goal(goalModel));
                return StatusCode((int)HttpStatusCode.Created);
            }
            else
            {
                goal.Minutes = Minutes;
                _context.GoalTime.Update(goal);
                await _context.SaveChangesAsync();

                return Ok(new Goal(goal));
            }
        }

        /// <summary>
        ///   Gets a list of all <see cref="Goal"/>s that match the given classification.
        /// </summary>
        /// <param name="ChannelName"></param>
        /// <param name="DefinitionId"></param>
        [HttpGet]
        [SwaggerApiResponse(HttpStatusCode.OK, Type = typeof(Goal), Description = "Get the Goal fo")]
        [ValidateModelState]
        public virtual async Task<IActionResult> GetGoalTimes([Required]int DefinitionId, [Required]string ChannelName)
        {
            Data.Models.Channel channel = await _context.Channels
                    .Where(c => c.Name.Equals(ChannelName)).FirstOrDefaultAsync();
            Data.Models.GoalTime goal = await _context.GoalTime
            .Where(g => g.DefinitionId == DefinitionId && g.ChannelId == channel.Id).FirstOrDefaultAsync();
            return Ok(goal); ;
        }


    }
}
