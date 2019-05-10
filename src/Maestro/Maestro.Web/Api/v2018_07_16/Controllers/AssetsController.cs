// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Threading.Tasks;
using Maestro.Data;
using Maestro.Web.Api.v2018_07_16.Models;
using Microsoft.AspNetCore.ApiPagination;
using Microsoft.AspNetCore.ApiVersioning;
using Microsoft.AspNetCore.ApiVersioning.Swashbuckle;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.DotNet.DarcLib;
using Microsoft.EntityFrameworkCore;

namespace Maestro.Web.Api.v2018_07_16.Controllers
{
    /// <summary>
    ///   Exposes methods to Read/Query <see cref="Asset"/>s and modify <see cref="AssetLocation"/> information
    /// </summary>
    [Route("assets")]
    [ApiVersion("2018-07-16")]
    public class AssetsController : Controller
    {
        private readonly BuildAssetRegistryContext _context;

        public AssetsController(BuildAssetRegistryContext context)
        {
            _context = context;
        }

        /// <summary>
        ///   Gets a paged list of all <see cref="Asset"/>s that match the given search criteria.
        /// </summary>
        /// <param name="name"></param>
        /// <param name="version"></param>
        /// <param name="buildId"></param>
        /// <param name="nonShipping"></param>
        /// <param name="loadLocations">**true** to include the Asset Location data with the response; **false** otherwise.</param>
        [HttpGet]
        [SwaggerApiResponse(HttpStatusCode.OK, Type = typeof(List<Asset>), Description = "List of Assets")]
        [Paginated(typeof(Models.Asset))]
        [ValidateModelState]
        public IActionResult ListAssets(string name, [FromQuery] string version, int? buildId, bool? nonShipping, bool? loadLocations)
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

            query = query.OrderByDescending(a => a.Id);
            return Ok(query);
        }

        /// <summary>
        ///   Gets the version of Darc in use by this deployment of Maestro.
        /// </summary>
        [HttpGet("darc-version")]
        [SwaggerApiResponse(HttpStatusCode.OK, Type = typeof(string), Description = "Gets the version of darc in use by this Maestro++ instance.")]
        [ValidateModelState]
        [AllowAnonymous]
        public IActionResult GetDarcVersion()
        {
            // Use the assembly file version, which is the same as the package
            // version. The informational version has a "+<sha>" appended to the end for official builds
            // We don't want this, so eliminate it. The primary use of this is to install the darc version
            // corresponding to the maestro++ version.
            AssemblyInformationalVersionAttribute informationalVersionAttribute =
                typeof(IRemote).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
            string version = informationalVersionAttribute.InformationalVersion;
            int lastPlus = version.LastIndexOf('+');
            if (lastPlus != -1)
            {
                version = version.Substring(0, lastPlus);
            }
            return Ok(version);
        }

        /// <summary>
        ///   Gets a single <see cref="Asset"/>, including all <see cref="AssetLocation"/>s.
        /// </summary>
        /// <param name="id">The id of the <see cref="Asset"/>.</param>
        [HttpGet("{id}")]
        [SwaggerApiResponse(HttpStatusCode.OK, Type = typeof(Asset), Description = "The requested Asset")]
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

        /// <summary>
        ///   Adds a new <see cref="AssetLocation"/> to an existing <see cref="Asset"/> object.
        /// </summary>
        /// <param name="assetId">The id of the <see cref="Asset"/> to add the <see cref="AssetLocation"/> to.</param>
        /// <param name="location">The location to add to the Asset.</param>
        /// <param name="assetLocationType">The type of the location.</param>
        [HttpPost("{assetId}/locations")]
        [SwaggerApiResponse(HttpStatusCode.Created, Type = typeof(AssetLocation), Description = "AssetLocation successfully added")]
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

        /// <summary>
        ///   Removes an existing <see cref="AssetLocation"/> from an <see cref="Asset"/>.
        /// </summary>
        /// <param name="assetId">The id of the <see cref="Asset"/> to remove the <see cref="AssetLocation"/> from.</param>
        /// <param name="assetLocationId">The id of the <see cref="AssetLocation"/> to remove.</param>
        [HttpDelete("{assetId}/locations/{assetLocationId}")]
        [SwaggerApiResponse(HttpStatusCode.OK, Description = "AssetLocation successfully removed")]
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
