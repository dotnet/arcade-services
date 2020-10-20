// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Maestro.Data;
using Maestro.Web.Api.v2020_02_20.Models;
using Microsoft.AspNetCore.ApiVersioning;
using Microsoft.AspNetCore.ApiVersioning.Swashbuckle;
using Microsoft.AspNetCore.Mvc;
using Microsoft.DotNet.Services.Utility;
using Microsoft.EntityFrameworkCore;
using Channel = Maestro.Data.Models.Channel;

namespace Maestro.Web.Api.v2020_02_20.Controllers
{
    /// <summary>
    ///   Exposes methods to Create/Read/Delete <see cref="DefaultChannel"/> mapping information.
    /// </summary>
    [Route("default-channels")]
    [ApiVersion("2020-02-20")]
    public class DefaultChannelsController : v2018_07_16.Controllers.DefaultChannelsController
    {
        private readonly BuildAssetRegistryContext _context;
        // Branch names can't possibly start with -, so we'll use this fact to guarantee the user 
        // wants to use a regex, and not direct matching.
        private const string _regexBranchPrefix = "-regex:";

        public DefaultChannelsController(BuildAssetRegistryContext context)
            : base(context)
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
        public override IActionResult List(string repository = null, string branch = null, int? channelId = null, bool? enabled = null)
        {
            IQueryable<Data.Models.DefaultChannel> query = _context.DefaultChannels.Include(dc => dc.Channel)
                .AsNoTracking();

            if (!string.IsNullOrEmpty(repository))
            {
                query = query.Where(dc => dc.Repository == repository);
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

            if (!string.IsNullOrEmpty(branch))
            {
                List<DefaultChannel> branchFilteredResults = new List<DefaultChannel>();
                foreach (DefaultChannel defaultChannel in results)
                {
                    // Branch name expressed as a regular expression: must start with '-regex:' and have at least one more character.
                    // - Skips NormalizeBranchName here because internally everything is stored without that.
                    //   If there's a pattern of users doing '-regex:/refs/heads/release.*' this could be revisited.
                    if (defaultChannel.Branch.StartsWith(_regexBranchPrefix, StringComparison.InvariantCultureIgnoreCase) &&
                        defaultChannel.Branch.Length > _regexBranchPrefix.Length &&
                        new Regex(defaultChannel.Branch.Substring(_regexBranchPrefix.Length)).IsMatch(branch))
                    {
                        branchFilteredResults.Add(defaultChannel);
                    }
                    else if (defaultChannel.Branch == GitHelpers.NormalizeBranchName(branch))
                    {
                        branchFilteredResults.Add(defaultChannel);
                    }
                }
                return Ok(branchFilteredResults);
            }
            return Ok(results);
        }

        [ApiRemoved]
        public override Task<IActionResult> Create([FromBody, Required] v2018_07_16.Models.DefaultChannel.DefaultChannelCreateData data)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        ///   Creates a <see cref="DefaultChannel"/> mapping.
        /// </summary>
        /// <param name="data">An object containing the data for the new <see cref="DefaultChannel"/></param>
        [HttpPost]
        [SwaggerApiResponse(HttpStatusCode.Created, Description = "DefaultChannel successfully created")]
        [SwaggerApiResponse(HttpStatusCode.Conflict, Description = "A DefaultChannel matching the data already exists")]
        [ValidateModelState]
        public async Task<IActionResult> Create([FromBody, Required] DefaultChannel.DefaultChannelCreateData data)
        {
            int channelId = data.ChannelId;
            Channel channel = await _context.Channels.FindAsync(channelId);
            if (channel == null)
            {
                return NotFound(new ApiError($"The channel with id '{channelId}' was not found."));
            }

            Data.Models.DefaultChannel defaultChannel;

            // Due to abundant retry logic, we'll return a normal response even if this is creating a duplicate, by simply
            // returning the one that already exists vs. HTTP 409 / 500
            var existingInstance = _context.DefaultChannels
                .Where(d => d.Channel == channel &&
                            d.Repository == data.Repository &&
                            d.Branch == data.Branch)
                .FirstOrDefault();

            if (existingInstance != null)
            {
                defaultChannel = existingInstance;
            }
            else
            {
                defaultChannel = new Data.Models.DefaultChannel
                {
                    Channel = channel,
                    Repository = data.Repository,
                    Branch = data.Branch,
                    Enabled = data.Enabled ?? true
                };
                await _context.DefaultChannels.AddAsync(defaultChannel);
                await _context.SaveChangesAsync();
            }
            return CreatedAtRoute(
                new
                {
                    action = "Get",
                    id = defaultChannel.Id
                },
                new DefaultChannel(defaultChannel));
        }

        [ApiRemoved]
        public override Task<IActionResult> Update(int id, [FromBody] v2018_07_16.Models.DefaultChannel.DefaultChannelUpdateData update)
        {
            throw new NotImplementedException();
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
        public override async Task<IActionResult> Get(int id)
        {
            Data.Models.DefaultChannel defaultChannel = await _context.DefaultChannels.FindAsync(id);
            if (defaultChannel == null)
            {
                return NotFound();
            }

            return Ok(new DefaultChannel(defaultChannel));
        }

    }
}
