// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Maestro.Data;
using Maestro.Web.Api.v2019_01_16.Models;
using Microsoft.AspNetCore.ApiPagination;
using Microsoft.AspNetCore.ApiVersioning;
using Microsoft.AspNetCore.ApiVersioning.Swashbuckle;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

namespace Maestro.Web.Api.v2019_01_16.Controllers
{
    /// <summary>
    ///   Exposes methods to Read/Query/Create <see cref="Build"/>s.
    /// </summary>
    [Route("builds")]
    [ApiVersion("2019-01-16")]
    public class BuildsController : v2018_07_16.Controllers.BuildsController
    {
        public BuildsController(BuildAssetRegistryContext context)
            : base(context)
        {
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
        /// <param name="loadCollections">**true** to include the <see cref="v2018_07_16.Models.Channel"/>, <see cref="v2018_07_16.Models.Asset"/>, and dependent <see cref="Build"/> data with the response; **false** otherwise.</param>
        [HttpGet]
        [SwaggerApiResponse(HttpStatusCode.OK, Type = typeof(List<Build>), Description = "The list of Builds matching the search criteria")]
        [Paginated(typeof(Build))]
        [ValidateModelState]
        public override IActionResult ListBuilds(
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

        /// <summary>
        ///   Gets a single <see cref="Build"/>, including all the <see cref="v2018_07_16.Models.Channel"/>, <see cref="v2018_07_16.Models.Asset"/>, and dependent <see cref="Build"/> data.
        /// </summary>
        /// <param name="id">The id of the <see cref="Build"/>.</param>
        [HttpGet("{id}")]
        [SwaggerApiResponse(HttpStatusCode.OK, Type = typeof(Build), Description = "The requested Build")]
        [ValidateModelState]
        public override async Task<IActionResult> GetBuild(int id)
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

            return Ok(new Build(build));
        }

        [HttpGet("{id}/graph")]
        [SwaggerApiResponse(HttpStatusCode.OK, Type = typeof(BuildGraph), Description = "The tree of build dependencies")]
        [ValidateModelState]
        public async Task<IActionResult> GetBuildGraph(int id)
        {
            Data.Models.Build build = await _context.Builds.FirstOrDefaultAsync(b => b.Id == id);

            if (build == null)
            {
                return NotFound();
            }

            var builds = await _context.GetBuildGraphAsync(build.Id);

            return Ok(BuildGraph.Create(builds.Select(b => new Build(b))));
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
        /// <param name="loadCollections">**true** to include the <see cref="v2018_07_16.Models.Channel"/>, <see cref="v2018_07_16.Models.Asset"/>, and dependent <see cref="Build"/> data with the response; **false** otherwise.</param>
        [HttpGet("latest")]
        [SwaggerApiResponse(HttpStatusCode.OK, Type = typeof(Build), Description = "The latest Build matching the search criteria")]
        [ValidateModelState]
        public override async Task<IActionResult> GetLatest(
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

            return Ok(new Models.Build(build));
        }

        [ApiRemoved]
        public sealed override Task<IActionResult> Create(v2018_07_16.Models.BuildData build)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        ///   Creates a new <see cref="Build"/> in the database.
        /// </summary>
        /// <param name="build">An object containing the data for the new <see cref="Build"/></param>
        [HttpPost]
        [SwaggerApiResponse(HttpStatusCode.Created, Type = typeof(Build), Description = "The created build")]
        [ValidateModelState]
        public async Task<IActionResult> Create([FromBody, Required] BuildData build)
        {
            Data.Models.Build buildModel = build.ToDb();
            buildModel.DateProduced = DateTimeOffset.UtcNow;
            if (build.Dependencies != null)
            {
                // For each Dependency, update the time to Inclusion
                foreach (var dep in build.Dependencies)
                {
                    // Find the dependency in BuildDependencies table where the build it is associated with
                    // matches the repository and branch of the current build
                    var buildDependency = _context.BuildDependencies.Where( d =>
                            d.DependentBuildId == dep.BuildId &&
                            d.Build.GitHubRepository == buildModel.GitHubRepository &&
                            d.Build.GitHubBranch == buildModel.GitHubBranch &&
                            d.Build.AzureDevOpsRepository == buildModel.AzureDevOpsRepository &&
                            d.Build.AzureDevOpsBranch == buildModel.AzureDevOpsBranch
                        ).FirstOrDefault();

                    if (buildDependency != null && buildDependency.TimeToInclusionInMinutes != 0)
                    {
                        dep.TimeToInclusionInMinutes = buildDependency.TimeToInclusionInMinutes;
                    }
                    else if (buildDependency != null && buildDependency.TimeToInclusionInMinutes == 0)
                    {
                        // Calculate the buildDependency's time to inclusion, and use that
                        Data.Models.Build originalBuild = await _context.Builds.FindAsync(buildDependency.BuildId);
                        Data.Models.Build depBuild = await _context.Builds.FindAsync(dep.BuildId);
                        dep.TimeToInclusionInMinutes = (originalBuild.DateProduced - depBuild.DateProduced).TotalMinutes;
                    }
                    else
                    {
                        // If the dependent build is not currently in the BuildDependency table for this repo/branch (ie is a new dependency),
                        // find the dependency in the Builds table and calculate the time to inclusion
                        Data.Models.Build depBuild = await _context.Builds.FindAsync(dep.BuildId);
                        dep.TimeToInclusionInMinutes = (buildModel.DateProduced - depBuild.DateProduced).TotalMinutes;
                    }
                }

                await _context.BuildDependencies.AddRangeAsync(
                    build.Dependencies.Select(
                        b => new Data.Models.BuildDependency
                        {
                            Build = buildModel, DependentBuildId = b.BuildId, IsProduct = b.IsProduct, TimeToInclusionInMinutes = b.TimeToInclusionInMinutes,
                        }));
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
