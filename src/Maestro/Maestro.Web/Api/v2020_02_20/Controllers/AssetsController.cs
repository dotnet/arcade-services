// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Maestro.Data;
using Maestro.Web.Api.v2018_07_16.Models;
using Maestro.Web.Api.v2020_02_20.Models;
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
            var errorsToReport = new List<string>();
            var groupsOfAssetsToUpdate = updates.GroupBy(upd => upd.AssetId);

            foreach (var assetToUpdate in groupsOfAssetsToUpdate)
            {
                var asset = await _context.Assets
                    .Include(a => a.Locations)
                    .Where(a => a.Id == assetToUpdate.Key)
                    .SingleOrDefaultAsync();

                if (asset == null)
                {
                    errorsToReport.Add($"The asset with id '{assetToUpdate.Key}' was not found.");
                    continue;
                }

                foreach (var assetNewLocations in assetToUpdate)
                {
                    var assetLocation = new Data.Models.AssetLocation
                    {
                        Location = assetNewLocations.Location,
                        Type = (Data.Models.LocationType)assetNewLocations.LocationType,
                    };

                    // If asset location is already in the asset skip to next asset location
                    if (asset.Locations != null &&
                        asset.Locations.Any(existing => existing.Location.Equals(assetLocation.Location, StringComparison.OrdinalIgnoreCase) &&
                        existing.Type == assetLocation.Type))
                    {
                        continue;
                    }

                    asset.Locations = asset.Locations ?? new List<Data.Models.AssetLocation>();
                    asset.Locations.Add(assetLocation);
                    _context.Assets.Update(asset);
                }
            }

            if (errorsToReport.Any())
            {
                return NotFound(new ApiError("Error adding asset locations.", errorsToReport));
            }

            await _context.SaveChangesAsync();

            return StatusCode((int)HttpStatusCode.Created);
        }
    }
}
