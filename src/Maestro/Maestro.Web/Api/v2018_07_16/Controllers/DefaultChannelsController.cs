// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Maestro.Data;
using Maestro.Web.Api.v2018_07_16.Models;
using Microsoft.AspNetCore.ApiVersioning;
using Microsoft.AspNetCore.ApiVersioning.Swashbuckle;
using Microsoft.AspNetCore.Mvc;
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
        /// <returns></returns>
        [HttpGet]
        [SwaggerApiResponse(HttpStatusCode.OK, Type = typeof(List<DefaultChannel>), Description = "The list of DefaultChannels")]
        public IActionResult List(string repository = null, string branch = null, int? channelId = null)
        {
            IQueryable<Data.Models.DefaultChannel> query = _context.DefaultChannels.Include(dc => dc.Channel)
                .AsNoTracking();

            if (!string.IsNullOrEmpty(repository))
            {
                query = query.Where(dc => dc.Repository == repository);
            }

            if (!string.IsNullOrEmpty(branch))
            {
                query = query.Where(dc => dc.Branch == branch);
            }

            if (channelId.HasValue)
            {
                query = query.Where(dc => dc.ChannelId == channelId.Value);
            }

            List<DefaultChannel> results = query.AsEnumerable().Select(dc => new DefaultChannel(dc)).ToList();
            return Ok(results);
        }

        /// <summary>
        ///   Creates a <see cref="DefaultChannel"/> mapping.
        /// </summary>
        /// <param name="data">An object containing the data for the new <see cref="DefaultChannel"/></param>
        /// <returns></returns>
        [HttpPost]
        [SwaggerApiResponse(HttpStatusCode.Created, Description = "DefaultChannel successfully created")]
        [SwaggerApiResponse(HttpStatusCode.Conflict, Description = "A DefaultChannel matching the data already exists")]
        [ValidateModelState]
        [HandleDuplicateKeyRows("A default channel with the same (repository, branch, channel) already exists.")]
        public async Task<IActionResult> Create([FromBody] DefaultChannel.PostData data)
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
                Branch = data.Branch
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
        ///   Gets a single <see cref="DefaultChannel"/>.
        /// </summary>
        /// <param name="id">The id of the <see cref="DefaultChannel"/></param>
        /// <returns></returns>
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
        /// <returns></returns>
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
