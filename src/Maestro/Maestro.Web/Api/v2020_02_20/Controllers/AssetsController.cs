// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Maestro.Data;
using Maestro.Web.Api.v2018_07_16.Models;
using Maestro.Web.Api.v2020_02_20.Models;
using Microsoft.AspNetCore.ApiVersioning;
using Microsoft.AspNetCore.ApiVersioning.Swashbuckle;
using Microsoft.AspNetCore.Mvc;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Threading.Tasks;

namespace Maestro.Web.Api.v2020_02_20.Controllers
{
    /// <summary>
    ///   Exposes methods to Read/Query <see cref="Asset"/>s and modify <see cref="AssetLocation"/> information
    /// </summary>
    [Route("assets")]
    [ApiVersion("2020-02-20")]
    public class AssetsController : v2018_07_16.Controllers.AssetsController
    {
        public AssetsController(BuildAssetRegistryContext context)
            : base (context)
        {
        }

        /// <summary>
        ///   Receive a list of pairs of AssetId and AssetLocation and persist them on BAR.
        /// </summary>
        /// <param name="updates">A list of AssetId and Location that should be persisted.</param>
        [HttpPost("bulk-add-locations")]
        [SwaggerApiResponse(HttpStatusCode.Created, Description = "AssetLocation successfully added")]
        public async Task<IActionResult> BulkAddLocations([Required, FromBody] IEnumerable<AssetAndLocation> updates)
        {
            foreach (AssetAndLocation update in updates)
            {
                IActionResult result = await AddAssetLocationToAsset(update.AssetId, update.Location, update.LocationType);

                if (result is NotFoundResult)
                {
                    return NotFound(new ApiError($"The asset with id '{update.AssetId}' was not found."));
                }
            }

            return StatusCode((int)HttpStatusCode.Created);
        }
    }
}
