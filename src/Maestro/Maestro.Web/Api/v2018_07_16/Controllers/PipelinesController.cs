// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Maestro.Data;
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
using ReleasePipeline = Maestro.Web.Api.v2018_07_16.Models.ReleasePipeline;

namespace Maestro.Web.Api.v2018_07_16.Controllers
{
    /// <summary>
    ///   Exposes methods to Create/Read/Delete <see cref="ReleasePipeline"/> information.
    /// </summary>
    [Route("pipelines")]
    [ApiVersion("2018-07-16")]
    public class PipelinesController : Controller
    {
        private readonly BuildAssetRegistryContext _context;

        public PipelinesController(BuildAssetRegistryContext context)
        {
            _context = context;
        }

        /// <summary>
        ///   Gets a list of all <see cref="ReleasePipeline"/>s that match the given search criteria.
        /// </summary>
        /// <param name="pipelineIdentifier">The Azure DevOps Release Pipeline id</param>
        /// <param name="organization">The Azure DevOps organization</param>
        /// <param name="project">The Azure DevOps project</param>
        [HttpGet]
        [SwaggerApiResponse(HttpStatusCode.OK, Type = typeof(List<ReleasePipeline>), Description = "The list of ReleasePipelines")]
        [ValidateModelState]
        public virtual IActionResult List(int? pipelineIdentifier = null, string organization = null, string project = null)
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

        /// <summary>
        ///   Gets a single <see cref="ReleasePipeline"/>.
        /// </summary>
        /// <param name="id">The id of the <see cref="ReleasePipeline"/> to get</param>
        [HttpGet("{id}")]
        [SwaggerApiResponse(HttpStatusCode.OK, Type = typeof(ReleasePipeline), Description = "The requested ReleasePipeline")]
        [ValidateModelState]
        public virtual async Task<IActionResult> GetPipeline(int id)
        {
            Data.Models.ReleasePipeline pipeline = await _context.ReleasePipelines.Where(c => c.Id == id).FirstOrDefaultAsync();

            if (pipeline == null)
            {
                return NotFound();
            }

            return Ok(new ReleasePipeline(pipeline));
        }

        /// <summary>
        ///   Deletes a <see cref="ReleasePipeline"/>
        /// </summary>
        /// <param name="id">The id of the <see cref="ReleasePipeline"/> to delete</param>
        [HttpDelete("{id}")]
        [SwaggerApiResponse(HttpStatusCode.OK, Type = typeof(ReleasePipeline), Description = "ReleasePipeline successfully deleted")]
        [ValidateModelState]
        public virtual async Task<IActionResult> DeletePipeline(int id)
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

        /// <summary>
        ///   Creates a <see cref="ReleasePipeline"/>
        /// </summary>
        /// <param name="pipelineIdentifier">The Azure DevOps Release Pipeline id</param>
        /// <param name="organization">The Azure DevOps organization</param>
        /// <param name="project">The Azure DevOps project</param>
        [HttpPost]
        [SwaggerApiResponse(HttpStatusCode.Created, Type = typeof(ReleasePipeline), Description = "ReleasePipeline successfully created")]
        public virtual async Task<IActionResult> CreatePipeline([Required] int pipelineIdentifier, [Required] string organization, [Required] string project)
        {
            Data.Models.ReleasePipeline pipeline = await _context.ReleasePipelines
                .FirstOrDefaultAsync(rp => 
                    rp.PipelineIdentifier == pipelineIdentifier && 
                    rp.Organization == organization && 
                    rp.Project == project
                );

            // If an release pipeline with same values already exist then do nothing
            if (pipeline != null)
            {
                return StatusCode((int)HttpStatusCode.NotModified);
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
