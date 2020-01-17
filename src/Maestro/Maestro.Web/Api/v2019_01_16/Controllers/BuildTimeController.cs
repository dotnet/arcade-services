// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Kusto.Data.Common;
using Maestro.Data;
using Maestro.Web.Api.v2019_01_16.Models;
using Microsoft.AspNetCore.ApiVersioning;
using Microsoft.AspNetCore.ApiVersioning.Swashbuckle;
using Microsoft.AspNetCore.Mvc;
using Microsoft.DotNet.Kusto;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

namespace Maestro.Web.Api.v2019_01_16.Controllers
{
    /// <summary>
    ///   Exposes methods to Read <see cref="BuildTime"/>s
    /// </summary>
    [Route("buildtime")]
    [ApiVersion("2019-01-16")]
    public class BuildTimeController : ControllerBase
    {
        protected readonly BuildAssetRegistryContext _context;
        private readonly KustoClientProvider _kustoClientProvider;

        public BuildTimeController(
            BuildAssetRegistryContext context,
            IKustoClientProvider kustoClientProvider)
        {
            _context = context;
            _kustoClientProvider = (KustoClientProvider) kustoClientProvider;
        }

        /// <summary>
        /// Gets the average official build time and average pr build time for a given default channel
        /// This is captured for generating the longest build times for the dependency flow graph
        /// </summary>
        /// <param name="id">Default channel id</param>
        /// <param name="days">Number of days to summarize over</param>
        [HttpGet("{id}")]
        [SwaggerApiResponse(HttpStatusCode.OK, Type = typeof(BuildTime), Description = "Gets the average official build time and average pr build time for a given default channel")]
        [ValidateModelState]
        public virtual async Task<IActionResult> GetBuildTimes([Required]int id, int days = 7)
        {
            Data.Models.DefaultChannel defaultChannel = await _context.DefaultChannels.FindAsync(id);

            if (defaultChannel == null)
            {
                return NotFound();
            }

            Dictionary<string, KustoQuery> queries = Helpers.CreateBuildTimesQueries(defaultChannel.Repository, defaultChannel.Branch, days);

            var results = await Task.WhenAll<TimeSpan>(_kustoClientProvider.GetSingleValueFromQueryAsync<TimeSpan>(queries["internal"]), 
                _kustoClientProvider.GetSingleValueFromQueryAsync<TimeSpan>(queries["public"]));

            double officialTime = results[0].TotalMinutes;
            double prTime = results[1].TotalMinutes;

            return Ok(new BuildTime(id, officialTime, prTime));
        }
    }
}
