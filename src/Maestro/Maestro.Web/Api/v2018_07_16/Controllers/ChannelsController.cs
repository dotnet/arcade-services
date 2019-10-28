// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Maestro.Data;
using Maestro.Data.Models;
using Microsoft.AspNetCore.ApiVersioning;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.ApiVersioning.Swashbuckle;
using Build = Maestro.Data.Models.Build;
using Channel = Maestro.Web.Api.v2018_07_16.Models.Channel;

namespace Maestro.Web.Api.v2018_07_16.Controllers
{
    /// <summary>
    ///   Exposes methods to Create/Read/Edit/Delete <see cref="Channel"/>s.
    /// </summary>
    [Route("channels")]
    [ApiVersion("2018-07-16")]
    public class ChannelsController : Controller
    {
        private readonly BuildAssetRegistryContext _context;

        public ChannelsController(BuildAssetRegistryContext context)
        {
            _context = context;
        }

        /// <summary>
        ///   Gets a list of all <see cref="Channel"/>s that match the given classification.
        /// </summary>
        /// <param name="classification">The <see cref="Channel.Classification"/> of <see cref="Channel"/> to get</param>
        [HttpGet]
        [SwaggerApiResponse(HttpStatusCode.OK, Type = typeof(List<Channel>), Description = "The list of Channels")]
        [ValidateModelState]
        public IActionResult ListChannels(string classification = null)
        {
            IQueryable<Data.Models.Channel> query = _context.Channels;
            if (!string.IsNullOrEmpty(classification))
            {
                query = query.Where(c => c.Classification == classification);
            }

            List<Channel> results = query.AsEnumerable().Select(c => new Channel(c)).ToList();
            return Ok(results);
        }

        [HttpGet("{id}/repositories")]
        [SwaggerApiResponse(HttpStatusCode.OK, Type = typeof(List<string>), Description = "List of repositories in Channel")]
        [ValidateModelState]
        public async Task<IActionResult> ListRepositories(int id)
        {
            List<string> list = await _context.BuildChannels
                    .Include(b => b.Build.GitHubRepository)
                    .Where(bc => bc.ChannelId == id)
                    .Select(bc => bc.Build.GitHubRepository)
                    .Where(b => !String.IsNullOrEmpty(b))
                    .Distinct()
                    .ToListAsync();
            return Ok(list);
        }

        /// <summary>
        ///   Gets a single <see cref="Channel"/>, including all <see cref="ReleasePipeline"/> data.
        /// </summary>
        /// <param name="id">The id of the <see cref="Channel"/> to get</param>
        [HttpGet("{id}")]
        [SwaggerApiResponse(HttpStatusCode.OK, Type = typeof(Channel), Description = "The requested Channel")]
        [ValidateModelState]
        public async Task<IActionResult> GetChannel(int id)
        {
            Data.Models.Channel channel = await _context.Channels
                .Include(ch => ch.ChannelReleasePipelines)
                .ThenInclude(crp => crp.ReleasePipeline)
                .Where(c => c.Id == id).FirstOrDefaultAsync();

            if (channel == null)
            {
                return NotFound();
            }

            return Ok(new Channel(channel));
        }

        /// <summary>
        ///   Deletes a <see cref="Channel"/>.
        /// </summary>
        /// <param name="id">The id of the <see cref="Channel"/> to delete</param>
        [HttpDelete("{id}")]
        [SwaggerApiResponse(HttpStatusCode.OK, Type = typeof(Channel), Description = "The Channel has been deleted")]
        [ValidateModelState]
        public async Task<IActionResult> DeleteChannel(int id)
        {
            Data.Models.Channel channel = await _context.Channels
                .Include(ch => ch.ChannelReleasePipelines)
                .FirstOrDefaultAsync(c => c.Id == id);

            if (channel == null)
            {
                return NotFound();
            }

            // Ensure that there are no subscriptions associated with the channel
            if (await _context.Subscriptions.AnyAsync(s => s.ChannelId == id))
            {
                return BadRequest(
                    new ApiError($"The channel with id '{id}' has associated subscriptions. " +
                    "Please remove these before removing this channel."));
            }

            if (channel.ChannelReleasePipelines != null && channel.ChannelReleasePipelines.Any())
            {
                return BadRequest(
                    new ApiError($"The channel with id '{id}' has '{channel.ChannelReleasePipelines.Count()}' " +
                    $"release pipeline(s) attached to it. Detach those release pipelines(s) first."));
            }

            _context.Channels.Remove(channel);

            await _context.SaveChangesAsync();
            return Ok(new Channel(channel));
        }

        /// <summary>
        ///   Creates a <see cref="Channel"/>.
        /// </summary>
        /// <param name="name">The name of the new <see cref="Channel"/>. This is required to be unique.</param>
        /// <param name="classification">The classification of the new <see cref="Channel"/></param>
        [HttpPost]
        [SwaggerApiResponse(HttpStatusCode.Created, Type = typeof(Channel), Description = "The Channel has been created")]
        [SwaggerApiResponse(HttpStatusCode.Conflict, Description = "A Channel with that name already exists.")]
        [HandleDuplicateKeyRows("Could not create channel '{name}'. A channel with the specified name already exists.")]
        public async Task<IActionResult> CreateChannel([Required] string name, [Required] string classification)
        {
            var channelModel = new Data.Models.Channel
            {
                Name = name,
                Classification = classification
            };
            await _context.Channels.AddAsync(channelModel);
            await _context.SaveChangesAsync();
            return CreatedAtRoute(
                new
                {
                    action = "GetChannel",
                    id = channelModel.Id
                },
                new Channel(channelModel));
        }

        /// <summary>
        ///   Adds an existing <see cref="Build"/> to the specified <see cref="Channel"/>
        /// </summary>
        /// <param name="channelId">The id of the <see cref="Channel"/>.</param>
        /// <param name="buildId">The id of the <see cref="Build"/></param>
        [HttpPost("{channelId}/builds/{buildId}")]
        [SwaggerApiResponse(HttpStatusCode.Created, Description = "Build successfully added to the Channel")]
        [HandleDuplicateKeyRows("Build {buildId} is already in channel {channelId}")]
        public async Task<IActionResult> AddBuildToChannel(int channelId, int buildId)
        {
            Data.Models.Channel channel = await _context.Channels.FindAsync(channelId);
            if (channel == null)
            {
                return NotFound(new ApiError($"The channel with id '{channelId}' was not found."));
            }

            Build build = await _context.Builds.FindAsync(buildId);
            if (build == null)
            {
                return NotFound(new ApiError($"The build with id '{buildId}' was not found."));
            }

            // If build is already in channel, nothing to do
            if (build.BuildChannels != null &&
                build.BuildChannels.Any(existingBuildChannels => existingBuildChannels.ChannelId == channelId))
            {
                return StatusCode((int)HttpStatusCode.Created);
            }

            var buildChannel = new BuildChannel
            {
                Channel = channel,
                Build = build,
                DateTimeAdded = DateTimeOffset.UtcNow
            };
            await _context.BuildChannels.AddAsync(buildChannel);
            await _context.SaveChangesAsync();
            return StatusCode((int)HttpStatusCode.Created);
        }

        /// <summary>
        ///   Add an existing <see cref="ReleasePipeline"/> to the specified <see cref="Channel"/>
        /// </summary>
        /// <param name="channelId">The id of the <see cref="Channel"/></param>
        /// <param name="pipelineId">The id of the <see cref="ReleasePipeline"/></param>
        [HttpPost("{channelId}/pipelines/{pipelineId}")]
        [SwaggerApiResponse(HttpStatusCode.Created, Description = "ReleasePipeline successfully added to Channel")]
        public async Task<IActionResult> AddPipelineToChannel(int channelId, int pipelineId)
        {
            Data.Models.Channel channel = await _context.Channels.FindAsync(channelId);
            if (channel == null)
            {
                return NotFound(new ApiError($"The channel with id '{channelId}' was not found."));
            }

            ReleasePipeline pipeline = await _context.ReleasePipelines
                .Include(rp => rp.ChannelReleasePipelines)
                .Where(rp => rp.Id == pipelineId)
                .SingleOrDefaultAsync();

            if (pipeline == null)
            {
                return NotFound(new ApiError($"The release pipeline with id '{pipelineId}' was not found."));
            }

            // If pipeline is already in channel, nothing to do
            if (pipeline.ChannelReleasePipelines != null &&
                pipeline.ChannelReleasePipelines.Any(existingPipelineChannel => existingPipelineChannel.ChannelId == channelId))
            {
                return StatusCode((int)HttpStatusCode.NotModified);
            }

            var pipelineChannel = new ChannelReleasePipeline
            {
                Channel = channel,
                ReleasePipeline = pipeline
            };
            await _context.ChannelReleasePipelines.AddAsync(pipelineChannel);
            await _context.SaveChangesAsync();
            return StatusCode((int)HttpStatusCode.Created);
        }

        /// <summary>
        ///   Remove a <see cref="ReleasePipeline"/> from the specified <see cref="Channel"/>
        /// </summary>
        /// <param name="channelId">The id of the <see cref="Channel"/></param>
        /// <param name="pipelineId">The id of the <see cref="ReleasePipeline"/></param>
        [HttpDelete("{channelId}/pipelines/{pipelineId}")]
        [SwaggerApiResponse(HttpStatusCode.OK, Description = "ReleasePipelines successfully removed from Channel")]
        public async Task<IActionResult> DeletePipelineFromChannel(int channelId, int pipelineId)
        {
            Data.Models.Channel channel = await _context.Channels
                .Include(ch => ch.ChannelReleasePipelines)
                .ThenInclude(crp => crp.ReleasePipeline)
                .Where(ch => ch.Id == channelId)
                .SingleOrDefaultAsync();
            
            if (channel == null)
            {
                return NotFound(new ApiError($"The channel with id '{channelId}' was not found."));
            }

            var pipeline = channel.ChannelReleasePipelines.Find(crp => crp.ReleasePipeline.Id == pipelineId);

            // If pipeline is not in the channel, nothing to do
            if (pipeline == null)
            {
                return StatusCode((int)HttpStatusCode.NotModified);
            }

            _context.ChannelReleasePipelines.Remove(pipeline);
            await _context.SaveChangesAsync();

            return StatusCode((int)HttpStatusCode.OK);
        }
    }
}
