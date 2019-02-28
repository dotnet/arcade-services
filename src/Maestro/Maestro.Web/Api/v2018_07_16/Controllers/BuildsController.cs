// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Maestro.Data;
using Maestro.Web.Api.v2018_07_16.Models;
using Microsoft.AspNetCore.ApiPagination;
using Microsoft.AspNetCore.ApiVersioning;
using Microsoft.AspNetCore.ApiVersioning.Swashbuckle;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Maestro.Web.Api.v2018_07_16.Controllers
{
    /// <summary>
    ///   Exposes methods to Read/Query/Create <see cref="Build"/>s.
    /// </summary>
    [Route("builds")]
    [ApiVersion("2018-07-16")]
    public class BuildsController : Controller
    {
        protected readonly BuildAssetRegistryContext _context;

        public BuildsController(BuildAssetRegistryContext context)
        {
            _context = context;
        }

        /// <summary>
        ///   Gets a paged list of all <see cref="Build"/>s that match the given search criteria.
        /// </summary>
        /// <param name="repository"></param>
        /// <param name="commit"></param>
        /// <param name="buildNumber"></param>
        /// <param name="channelId"></param>
        /// <param name="notBefore">Don't return <see cref="Build"/>s that happened before this time.</param>
        /// <param name="notAfter">Don't return <see cref="Build"/>s that happened after this time.</param>
        /// <param name="loadCollections">**true** to include the <see cref="Channel"/>, <see cref="Asset"/>, and dependent <see cref="Build"/> data with the response; **false** otherwise.</param>
        [HttpGet]
        [SwaggerApiResponse(HttpStatusCode.OK, Type = typeof(List<Build>), Description = "The list of Builds matching the search criteria")]
        [Paginated(typeof(Build))]
        [ValidateModelState]
        public virtual IActionResult ListBuilds(
            string repository,
            string commit,
            string buildNumber,
            int? channelId,
            DateTimeOffset? notBefore,
            DateTimeOffset? notAfter,
            bool? loadCollections)
        {
            IQueryable<Data.Models.Build> query = Query(
                repository,
                commit,
                buildNumber,
                channelId,
                notBefore,
                notAfter,
                loadCollections);
            return Ok(query);
        }

        protected IQueryable<Data.Models.Build> Query(
            string repository,
            string commit,
            string buildNumber,
            int? channelId,
            DateTimeOffset? notBefore,
            DateTimeOffset? notAfter,
            bool? loadCollections)
        {
            IQueryable<Data.Models.Build> query = _context.Builds;
            if (!string.IsNullOrEmpty(repository))
            {
                query = query.Where(b => (repository == b.GitHubRepository || repository == b.AzureDevOpsRepository));
            }

            if (!string.IsNullOrEmpty(commit))
            {
                query = query.Where(b => b.Commit == commit);
            }

            if (!string.IsNullOrEmpty(buildNumber))
            {
                query = query.Where(b => b.AzureDevOpsBuildNumber == buildNumber);
            }

            if (notBefore.HasValue)
            {
                query = query.Where(b => b.DateProduced >= notBefore.Value);
            }

            if (notAfter.HasValue)
            {
                query = query.Where(b => b.DateProduced <= notAfter.Value);
            }

            if (channelId.HasValue)
            {
                query = query.Where(b => b.BuildChannels.Any(c => c.ChannelId == channelId.Value));
            }

            if (loadCollections ?? false)
            {
                query = query
                    .Include(b => b.BuildChannels)
                    .ThenInclude(bc => bc.Channel)
                    .Include(b => b.Assets);
            }

            return query.OrderByDescending(b => b.DateProduced);
        }

        /// <summary>
        ///   Gets a single <see cref="Build"/>, including all the <see cref="Channel"/>, <see cref="Asset"/>, and dependent <see cref="Build"/> data.
        /// </summary>
        /// <param name="id">The id of the <see cref="Build"/>.</param>
        [HttpGet("{id}")]
        [SwaggerApiResponse(HttpStatusCode.OK, Type = typeof(Build), Description = "The requested Build")]
        [ValidateModelState]
        public virtual async Task<IActionResult> GetBuild(int id)
        {
            Data.Models.Build build = await _context.Builds.Where(b => b.Id == id)
                .Include(b => b.BuildChannels)
                .ThenInclude(bc => bc.Channel)
                .Include(b => b.Assets)
                .FirstOrDefaultAsync();

            if (build == null)
            {
                return NotFound();
            }

            return Ok(new Models.Build(build));
        }

        /// <summary>
        ///   Gets the latest <see cref="Build"/>s that matches the given search criteria.
        /// </summary>
        /// <param name="repository"></param>
        /// <param name="commit"></param>
        /// <param name="buildNumber"></param>
        /// <param name="channelId"></param>
        /// <param name="notBefore">Don't return <see cref="Build"/>s that happened before this time.</param>
        /// <param name="notAfter">Don't return <see cref="Build"/>s that happened after this time.</param>
        /// <param name="loadCollections">**true** to include the <see cref="Channel"/>, <see cref="Asset"/>, and dependent <see cref="Build"/> data with the response; **false** otherwise.</param>
        [HttpGet("latest")]
        [SwaggerApiResponse(HttpStatusCode.OK, Type = typeof(Build), Description = "The latest Build matching the search criteria")]
        [ValidateModelState]
        public virtual async Task<IActionResult> GetLatest(
            string repository,
            string commit,
            string buildNumber,
            int? channelId,
            DateTimeOffset? notBefore,
            DateTimeOffset? notAfter,
            bool? loadCollections)
        {
            IQueryable<Data.Models.Build> query = Query(
                repository,
                commit,
                buildNumber,
                channelId,
                notBefore,
                notAfter,
                loadCollections);
            Data.Models.Build build = await query.OrderByDescending(o => o.DateProduced).FirstOrDefaultAsync();
            if (build == null)
            {
                return NotFound();
            }

            return Ok(new Build(build));
        }

        /// <summary>
        ///   Creates a new <see cref="Build"/> in the database.
        /// </summary>
        /// <param name="build">An object containing the data for the new <see cref="Build"/></param>
        [HttpPost]
        [SwaggerApiResponse(HttpStatusCode.Created, Type = typeof(Build), Description = "The created build")]
        [ValidateModelState]
        public virtual async Task<IActionResult> Create([FromBody] BuildData build)
        {
            Data.Models.Build buildModel = build.ToDb();
            buildModel.DateProduced = DateTimeOffset.UtcNow;
            if (build.Dependencies?.Count > 0)
            {
                return BadRequest("This api version doesn't support build dependencies.");
            }
            await _context.Builds.AddAsync(buildModel);
            await _context.SaveChangesAsync();
            return CreatedAtRoute(
                new
                {
                    action = "GetBuild",
                    id = buildModel.Id
                },
                new Models.Build(buildModel));
        }
    }
}
