// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
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
using Swashbuckle.AspNetCore.Annotations;

namespace Maestro.Web.Api.v2018_07_16.Controllers
{
    [Route("assets")]
    [ApiVersion("2018-07-16")]
    public class AssetsController : Controller
    {
        private readonly BuildAssetRegistryContext _context;

        public AssetsController(BuildAssetRegistryContext context)
        {
            _context = context;
        }

        [HttpGet]
        [SwaggerApiResponse(HttpStatusCode.OK, Type = typeof(List<Asset>))]
        [Paginated(typeof(Models.Asset))]
        [ValidateModelState]
        public IActionResult Get(string name, [FromQuery] string version, int? buildId, bool? nonShipping, bool? loadLocations)
        {
            IQueryable<Data.Models.Asset> query = _context.Assets;
            if (!string.IsNullOrEmpty(name))
            {
                query = query.Where(asset => asset.Name == name);
            }

            if (!string.IsNullOrEmpty(version))
            {
                query = query.Where(asset => asset.Version == version);
            }

            if (buildId.HasValue)
            {
                query = query.Where(asset => asset.BuildId == buildId.Value);
            }

            if (nonShipping.HasValue)
            {
                query = query.Where(asset => asset.NonShipping == nonShipping.Value);
            }

            if (loadLocations ?? false)
            {
                query = query.Include(asset => asset.Locations);
            }

            return Ok(query);
        }

        [HttpGet("{id}")]
        [SwaggerApiResponse(HttpStatusCode.OK, Type = typeof(Asset))]
        [ValidateModelState]
        public async Task<IActionResult> GetAsset(int id)
        {
            Data.Models.Asset asset = await _context.Assets.Where(a => a.Id == id)
                .Include(a => a.Locations)
                .FirstOrDefaultAsync();

            if (asset == null)
            {
                return NotFound();
            }

            return Ok(new Asset(asset));
        }

        [HttpPost("{assetId}/locations")]
        [SwaggerApiResponse(HttpStatusCode.Created, Type = typeof(AssetLocation))]
        public async Task<IActionResult> AddAssetLocationToAsset(int assetId, [Required] string location, [Required] LocationType assetLocationType)
        {
            var assetLocation = new Data.Models.AssetLocation
            {
                Location = location,
                Type = (Maestro.Data.Models.LocationType) assetLocationType,
            };

            Maestro.Data.Models.Asset asset = await _context.Assets
                .Include(a => a.Locations)
                .Where(a => a.Id == assetId)
                .SingleOrDefaultAsync();

            if (asset == null)
            {
                return NotFound(new ApiError($"The asset with id '{assetId}' was not found."));
            }

            // If asset location is already in asset, nothing to do
            if (asset.Locations != null &&
                asset.Locations.Any(existing => existing.Location.Equals(assetLocation.Location, StringComparison.OrdinalIgnoreCase) &&
                existing.Type == assetLocation.Type))
            {
                return StatusCode((int)HttpStatusCode.NotModified);
            }

            asset.Locations = asset.Locations ?? new List<Data.Models.AssetLocation>();
            asset.Locations.Add(assetLocation);

            await _context.SaveChangesAsync();
            return CreatedAtRoute(
                new
                {
                    action = "GetAsset",
                    id = assetLocation.Id
                },
                new AssetLocation(assetLocation));
        }

        [HttpDelete("{assetId}/locations/{assetLocationId}")]
        [SwaggerApiResponse(HttpStatusCode.OK)]
        public async Task<IActionResult> RemoveAssetLocationFromAsset(int assetId, int assetLocationId)
        {
            Maestro.Data.Models.Asset asset = await _context.Assets
                .Include(a => a.Locations)
                .Where(a => a.Id == assetId)
                .SingleOrDefaultAsync();

            if (asset == null)
            {
                return NotFound(new ApiError($"The asset with id '{assetId}' was not found."));
            }

            var assetLocation = asset.Locations.Find(al => al.Id == assetLocationId);

            // If asset location is not in the asset, nothing to do
            if (assetLocation == null)
            {
                return StatusCode((int)HttpStatusCode.NotModified);
            }

            _context.AssetLocations.Remove(assetLocation);
            await _context.SaveChangesAsync();

            return StatusCode((int)HttpStatusCode.OK);
        }
    }
}
