// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Maestro.Data;
using Maestro.Web.Api.v2019_01_16.Models;
using Microsoft.AspNetCore.ApiPagination;
using Microsoft.AspNetCore.ApiVersioning;
using Microsoft.AspNetCore.ApiVersioning.Swashbuckle;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Build = Maestro.Data.Models.Build;

namespace Maestro.Web.Api.v2019_01_16.Controllers
{
    [Route("builds")]
    [ApiVersion("2019-01-16")]
    public class BuildsController : v2018_07_16.Controllers.BuildsController
    {
        private readonly BuildAssetRegistryContext _context;

        public BuildsController(BuildAssetRegistryContext context)
            : base(context)
        {
            _context = context;
        }

        [HttpGet]
        [SwaggerApiResponse(HttpStatusCode.OK, Type = typeof(List<Models.Build>))]
        [Paginated(typeof(Models.Build))]
        [ValidateModelState]
        public override IActionResult GetAllBuilds(
            string repository,
            string commit,
            string buildNumber,
            int? channelId,
            DateTimeOffset? notBefore,
            DateTimeOffset? notAfter,
            bool? loadCollections)
        {
            IQueryable<Build> query = Query(
                repository,
                commit,
                buildNumber,
                channelId,
                notBefore,
                notAfter,
                loadCollections);
            return Ok(query);
        }

        [HttpGet("{id}")]
        [SwaggerApiResponse(HttpStatusCode.OK, Type = typeof(Models.Build))]
        [ValidateModelState]
        public override async Task<IActionResult> GetBuild(int id)
        {
            Build build = await _context.Builds.Where(b => b.Id == id)
                .Include(b => b.BuildChannels)
                .ThenInclude(bc => bc.Channel)
                .Include(b => b.Assets)
                .Include(b => b.Dependencies)
                .FirstOrDefaultAsync();

            if (build == null)
            {
                return NotFound();
            }

            return Ok(new Models.Build(build));
        }

        [HttpGet("latest")]
        [SwaggerApiResponse(HttpStatusCode.OK, Type = typeof(Models.Build))]
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
            IQueryable<Build> query = Query(
                repository,
                commit,
                buildNumber,
                channelId,
                notBefore,
                notAfter,
                loadCollections);
            Build build = await query.OrderByDescending(o => o.DateProduced).FirstOrDefaultAsync();
            if (build == null)
            {
                return NotFound();
            }

            return Ok(new Models.Build(build));
        }

        [ApiRemoved]
        public override sealed Task<IActionResult> Create(v2018_07_16.Models.BuildData build)
        {
            throw new NotImplementedException();
        }

        [HttpPost]
        [SwaggerApiResponse(HttpStatusCode.Created, Type = typeof(Models.Build))]
        [ValidateModelState]
        public async Task<IActionResult> Create([FromBody] BuildData build)
        {
            Build buildModel = build.ToDb();
            buildModel.DateProduced = DateTimeOffset.UtcNow;
            buildModel.Dependencies = build.Dependencies != null
                ? await _context.Builds.Where(b => build.Dependencies.Contains(b.Id)).ToListAsync()
                : null;
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
