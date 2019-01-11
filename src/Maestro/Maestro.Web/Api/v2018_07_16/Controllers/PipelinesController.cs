// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Maestro.Data;
using Microsoft.AspNetCore.ApiVersioning;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Swashbuckle.AspNetCore.Annotations;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using ReleasePipeline = Maestro.Web.Api.v2018_07_16.Models.ReleasePipeline;

namespace Maestro.Web.Api.v2018_07_16.Controllers
{
    [Route("pipelines")]
    [ApiVersion("2018-07-16")]
    public class PipelinesController : Controller
    {
        private readonly BuildAssetRegistryContext _context;

        public PipelinesController(BuildAssetRegistryContext context)
        {
            _context = context;
        }

        [HttpGet]
        [SwaggerResponse((int)HttpStatusCode.OK, Type = typeof(List<ReleasePipeline>))]
        [ValidateModelState]
        public IActionResult Get(int? pipelineIdentifier = null, string organization = null, string project = null)
        {
            IQueryable<Data.Models.ReleasePipeline> query = _context.ReleasePipelines;

            if (pipelineIdentifier != null)
            {
                query = query.Where(p => p.PipelineIdentifier == pipelineIdentifier);
            }

            if (!string.IsNullOrEmpty(organization))
            {
                query = query.Where(p => p.Organization == organization);
            }

            if (!string.IsNullOrEmpty(project))
            {
                query = query.Where(p => p.Project == project);
            }

            List<ReleasePipeline> results = query.AsEnumerable().Select(c => new ReleasePipeline(c)).ToList();
            return Ok(results);
        }

        [HttpGet("{id}")]
        [SwaggerResponse((int)HttpStatusCode.OK, Type = typeof(ReleasePipeline))]
        [ValidateModelState]
        public async Task<IActionResult> GetPipeline(int id)
        {
            Data.Models.ReleasePipeline pipeline = await _context.ReleasePipelines.Where(c => c.Id == id).FirstOrDefaultAsync();

            if (pipeline == null)
            {
                return NotFound();
            }

            return Ok(new ReleasePipeline(pipeline));
        }

        [HttpDelete("{id}")]
        [SwaggerResponse((int)HttpStatusCode.OK, Type = typeof(ReleasePipeline))]
        [SwaggerResponse((int)HttpStatusCode.BadRequest)]
        [SwaggerResponse((int)HttpStatusCode.NotFound)]
        [ValidateModelState]
        public async Task<IActionResult> DeletePipeline(int id)
        {
            bool isPipelineInUse = await _context.ChannelReleasePipelines.AnyAsync(crp => crp.ReleasePipelineId == id);

            if (isPipelineInUse)
            {
                return BadRequest(new ApiError($"The pipeline with id '{id}' is in use and cannot be deleted."));
            }

            Data.Models.ReleasePipeline pipeline = await _context.ReleasePipelines
                .FirstOrDefaultAsync(c => c.Id == id);

            if (pipeline == null)
            {
                return NotFound();
            }

            _context.ReleasePipelines.Remove(pipeline);

            await _context.SaveChangesAsync();
            return Ok(new ReleasePipeline(pipeline));
        }

        [HttpPost]
        [SwaggerResponse((int)HttpStatusCode.Created, Type = typeof(ReleasePipeline))]
        public async Task<IActionResult> CreatePipeline([Required] int pipelineIdentifier, [Required] string organization, [Required] string project)
        {
            Data.Models.ReleasePipeline pipeline = await _context.ReleasePipelines
                .FirstOrDefaultAsync(rp => 
                    rp.PipelineIdentifier == pipelineIdentifier && 
                    rp.Organization.Equals(organization, StringComparison.OrdinalIgnoreCase) && 
                    rp.Project.Equals(project, StringComparison.OrdinalIgnoreCase)
                );

            // If an release pipeline with same values already exist then do nothing
            if (pipeline != null)
            {
                return CreatedAtRoute(
                    new
                    {
                        action = "GetPipeline",
                        id = pipeline.Id
                    },
                    new ReleasePipeline(pipeline));
            }

            var pipelineModel = new Data.Models.ReleasePipeline
            {
                PipelineIdentifier = pipelineIdentifier,
                Organization = organization,
                Project = project
            };
            await _context.ReleasePipelines.AddAsync(pipelineModel);
            await _context.SaveChangesAsync();
            return CreatedAtRoute(
                new
                {
                    action = "GetPipeline",
                    id = pipelineModel.Id
                },
                new ReleasePipeline(pipelineModel));
        }
    }
}
