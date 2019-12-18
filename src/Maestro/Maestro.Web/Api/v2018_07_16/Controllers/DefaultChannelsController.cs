// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Maestro.Data;
using Maestro.Web.Api.v2018_07_16.Models;
using Microsoft.AspNetCore.ApiVersioning;
using Microsoft.AspNetCore.ApiVersioning.Swashbuckle;
using Microsoft.AspNetCore.Mvc;
using Microsoft.DotNet.Services.Utilities;
using Microsoft.EntityFrameworkCore;
using Channel = Maestro.Data.Models.Channel;

namespace Maestro.Web.Api.v2018_07_16.Controllers
{
    /// <summary>
    ///   Exposes methods to Create/Read/Delete <see cref="DefaultChannel"/> mapping information.
    /// </summary>
    [Route("default-channels")]
    [ApiVersion("2018-07-16")]
    public class DefaultChannelsController : Controller
    {
        private readonly BuildAssetRegistryContext _context;

        public DefaultChannelsController(BuildAssetRegistryContext context)
        {
            _context = context;
        }

        /// <summary>
        ///   Gets a list of all <see cref="DefaultChannel"/> mappings that match the given search criteria.
        /// </summary>
        /// <param name="repository"></param>
        /// <param name="branch"></param>
        /// <param name="channelId"></param>
        /// <param name="enabled">True if the default channel should be initially enabled or disabled.</param>
        [HttpGet]
        [SwaggerApiResponse(HttpStatusCode.OK, Type = typeof(List<DefaultChannel>), Description = "The list of DefaultChannels")]
        public IActionResult List(string repository = null, string branch = null, int? channelId = null, bool? enabled = null)
        {
            IQueryable<Data.Models.DefaultChannel> query = _context.DefaultChannels.Include(dc => dc.Channel)
                .AsNoTracking();

            if (!string.IsNullOrEmpty(repository))
            {
                query = query.Where(dc => dc.Repository == repository);
            }

            if (!string.IsNullOrEmpty(branch))
            {
                // Normalize the branch name to not include refs/heads
                string normalizedBranchName = GitHelpers.NormalizeBranchName(branch);
                query = query.Where(dc => dc.Branch == normalizedBranchName);
            }

            if (channelId.HasValue)
            {
                query = query.Where(dc => dc.ChannelId == channelId.Value);
            }

            if (enabled.HasValue)
            {
                query = query.Where(dc => dc.Enabled == enabled.Value);
            }

            List<DefaultChannel> results = query.AsEnumerable().Select(dc => new DefaultChannel(dc)).ToList();
            return Ok(results);
        }

        /// <summary>
        ///   Creates a <see cref="DefaultChannel"/> mapping.
        /// </summary>
        /// <param name="data">An object containing the data for the new <see cref="DefaultChannel"/></param>
        [HttpPost]
        [SwaggerApiResponse(HttpStatusCode.Created, Description = "DefaultChannel successfully created")]
        [SwaggerApiResponse(HttpStatusCode.Conflict, Description = "A DefaultChannel matching the data already exists")]
        [ValidateModelState]
        [HandleDuplicateKeyRows("A default channel with the same (repository, branch, channel) already exists.")]
        public async Task<IActionResult> Create([FromBody, Required] DefaultChannel.DefaultChannelCreateData data)
        {
            int channelId = data.ChannelId;
            Channel channel = await _context.Channels.FindAsync(channelId);
            if (channel == null)
            {
                return NotFound(new ApiError($"The channel with id '{channelId}' was not found."));
            }

            var defaultChannel = new Data.Models.DefaultChannel
            {
                Channel = channel,
                Repository = data.Repository,
                Branch = data.Branch,
                Enabled = data.Enabled ?? true
            };
            await _context.DefaultChannels.AddAsync(defaultChannel);
            await _context.SaveChangesAsync();
            return CreatedAtRoute(
                new
                {
                    action = "Get",
                    id = defaultChannel.Id
                },
                new DefaultChannel(defaultChannel));
        }

        /// <summary>
        ///     Update an existing default channel with new data.
        /// </summary>
        /// <param name="id">Id of default channel</param>
        /// <param name="update">Default channel update data</param>
        /// <returns>Updated default channel data.</returns>
        [HttpPatch("{id}")]
        [SwaggerApiResponse(HttpStatusCode.OK, Type = typeof(DefaultChannel), Description = "Default channel successfully updated")]
        [SwaggerApiResponse(HttpStatusCode.NotFound, Description = "The existing default channel does not exist.")]
        [SwaggerApiResponse(HttpStatusCode.Conflict, Description = "A DefaultChannel matching the data already exists")]
        [ValidateModelState]
        public async Task<IActionResult> Update(int id, [FromBody] DefaultChannel.DefaultChannelUpdateData update)
        {
            Data.Models.DefaultChannel defaultChannel = await _context.DefaultChannels.FindAsync(id);
            if (defaultChannel == null)
            {
                return NotFound();
            }

            bool doUpdate = false;
            if (!string.IsNullOrEmpty(update.Branch))
            {
                defaultChannel.Branch = update.Branch;
                doUpdate = true;
            }

            if (!string.IsNullOrEmpty(update.Repository))
            {
                defaultChannel.Repository = update.Repository;
                doUpdate = true;
            }

            if (update.ChannelId.HasValue)
            {
                int channelId = update.ChannelId.Value;
                Channel channel = await _context.Channels.FindAsync(channelId);
                if (channel == null)
                {
                    return NotFound(new ApiError($"The channel with id '{channelId}' was not found."));
                }

                defaultChannel.ChannelId = channelId;
                defaultChannel.Channel = channel;
                doUpdate = true;
            }

            if (update.Enabled.HasValue)
            {
                defaultChannel.Enabled = update.Enabled.Value;
                doUpdate = true;
            }

            if (doUpdate)
            {
                _context.DefaultChannels.Update(defaultChannel);
                await _context.SaveChangesAsync();
            }

            return Ok(new DefaultChannel(defaultChannel));
        }

        /// <summary>
        ///   Gets a single <see cref="DefaultChannel"/>.
        /// </summary>
        /// <param name="id">The id of the <see cref="DefaultChannel"/></param>
        [HttpGet("{id}")]
        [SwaggerApiResponse(HttpStatusCode.OK, Type = typeof(DefaultChannel), Description = "The requested DefaultChannel")]
        [ValidateModelState]
        public async Task<IActionResult> Get(int id)
        {
            Data.Models.DefaultChannel defaultChannel = await _context.DefaultChannels.FindAsync(id);
            if (defaultChannel == null)
            {
                return NotFound();
            }

            return Ok(new DefaultChannel(defaultChannel));
        }

        /// <summary>
        ///   Deleted a single <see cref="DefaultChannel"/>
        /// </summary>
        /// <param name="id">The id of the <see cref="DefaultChannel"/> to delete.</param>
        [HttpDelete("{id}")]
        [ValidateModelState]
        [SwaggerApiResponse(HttpStatusCode.Accepted, Description = "DefaultChannel successfully deleted")]
        public async Task<IActionResult> Delete(int id)
        {
            Data.Models.DefaultChannel defaultChannel = await _context.DefaultChannels.FindAsync(id);
            if (defaultChannel == null)
            {
                return NotFound();
            }

            _context.DefaultChannels.Remove(defaultChannel);
            await _context.SaveChangesAsync();
            return StatusCode((int) HttpStatusCode.Accepted);
        }
    }
}
