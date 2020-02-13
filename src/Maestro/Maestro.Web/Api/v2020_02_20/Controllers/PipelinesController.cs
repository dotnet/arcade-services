// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Maestro.Data;
using Microsoft.AspNetCore.ApiVersioning;
using Microsoft.AspNetCore.Mvc;
using System;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;

namespace Maestro.Web.Api.v2020_02_20.Controllers
{
    /// <summary>
    ///   We don't use Release Pipelines anymore.
    /// </summary>
    [Route("pipelines")]
    [ApiVersion("2020-02-20")]
    public class PipelinesController : v2018_07_16.Controllers.PipelinesController
    {
        public PipelinesController(BuildAssetRegistryContext context)
            : base(context)
        {
        }

        [ApiRemoved]
        public override IActionResult List(int? pipelineIdentifier = null, string organization = null, string project = null)
        {
            throw new NotImplementedException();
        }

        [ApiRemoved]
        public override Task<IActionResult> GetPipeline(int id)
        {
            throw new NotImplementedException();
        }

        [ApiRemoved]
        public override Task<IActionResult> DeletePipeline(int id)
        {
            throw new NotImplementedException();
        }

        [ApiRemoved]
        public override Task<IActionResult> CreatePipeline([Required] int pipelineIdentifier, [Required] string organization, [Required] string project)
        {
            throw new NotImplementedException();
        }
    }
}
