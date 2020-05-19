// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Maestro.Data;
using Microsoft.AspNetCore.ApiVersioning;
using Microsoft.AspNetCore.ApiVersioning.Swashbuckle;
using Microsoft.AspNetCore.Mvc;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Threading.Tasks;
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
            return Ok(new List<ReleasePipeline>());
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
            return await Task.FromResult(NotFound());
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
            return await Task.FromResult(StatusCode((int)HttpStatusCode.NotModified));
        }

        /// <summary>
        ///   Creates a <see cref="ReleasePipeline"/>
        /// </summary>
        /// <param name="pipelineIdentifier">The Azure DevOps Release Pipeline id</param>
        /// <param name="organization">The Azure DevOps organization</param>
        /// <param name="project">The Azure DevOps project</param>
        [HttpPost]
        [SwaggerApiResponse(HttpStatusCode.Created, Type = typeof(ReleasePipeline), Description = "ReleasePipeline successfully created")]
        public async virtual Task<IActionResult> CreatePipeline([Required] int pipelineIdentifier, [Required] string organization, [Required] string project)
        {
            return await Task.FromResult(StatusCode((int)HttpStatusCode.NotModified));
        }
    }
}
