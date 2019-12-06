// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.ApiVersioning;
using Microsoft.AspNetCore.ApiVersioning.Swashbuckle;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using Maestro.Web.Api.v2019_01_16.Models;
using Maestro.Data;
using Maestro.Data.Models;
using Microsoft.DotNet.Kusto;
using Kusto.Data.Common;

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
        private readonly IKustoClientProvider _kustoClientProvider;

        public BuildTimeController(
            BuildAssetRegistryContext context,
            IKustoClientProvider kustoClientProvider)
        {
            _context = context;
            _kustoClientProvider = kustoClientProvider;
        }

        /// <summary>
        /// Gets the average official build time and average pr build time for a given default channel
        /// This is captured for generating the longest build times for the dependency flow graph
        /// </summary>
        /// <param name="id">Default channel id</param>
        [HttpGet("{id}")]
        [SwaggerApiResponse(HttpStatusCode.OK, Type = typeof(BuildTime), Description = "Gets the average official build time and average pr build time for a given default channel")]
        [ValidateModelState]
        public virtual async Task<IActionResult> GetBuildTimes([Required]int id)
        {
            Data.Models.DefaultChannel defaultChannel = await _context.DefaultChannels.FindAsync(id);

            if (defaultChannel == null)
            {
                return NotFound();
            }

            var properties = new ClientRequestProperties();

            // There is a mismatch between how we store the names of the repositories in BAR
            // and how we store them in Kusto. Normalize to the last part of the url.
            properties.SetParameter("_Repository", defaultChannel.Repository.Split("/").Last());
            properties.SetParameter("_SourceBranch", defaultChannel.Branch);

            string queryParameters = String.Join(",", properties.Parameters.Select(p => $"{p.Key}:{KustoDataTypes.String}"));

            // We only care about builds that complete successfully or partially successfully 
            // from the given repository. We summarize duration of the builds over the last 7 days.
            // There are multiple different definitions that run in parallel, so we summarize
            // on the definition id and ultimately choose the definition that took the longest.
            string commonQueryText = @"| where Repository endswith _Repository
                | where Result != 'failed' and Result != 'canceled' 
                | where FinishTime > ago(7d) 
                | extend duration = FinishTime - StartTime 
                | summarize avg(duration) by DefinitionId
                | summarize max(avg_duration)";

            // We only want the pull request time from the public ci. We don't exclude on branch,
            // as all PRs come in as refs/heads/#/merge rather than what branch they are trying to
            // apply to.
            string publicQuery = $@"TimelineBuilds 
                | where Project == 'public'
                | where Reason == 'pullRequest' 
                {commonQueryText}";

            // For the official build times, we want the builds that were generated as a CI run 
            // (either batchedCI or individualCI) for a specific branch--i.e. we want the builds
            // that are part of generating the product.
            string internalQuery = $@"TimelineBuilds 
                | where Project == 'internal' 
                | where Reason contains 'CI'
                | where SourceBranch endswith _SourceBranch
                {commonQueryText}";
                
            double officialTime = 0;
            double prTime = 0;

            using (ICslQueryProvider query =
                    _kustoClientProvider.GetKustoQueryConnectionProvider())
            using (IDataReader result = await query.ExecuteQueryAsync(
                _kustoClientProvider.GetKustoDatabase(),
                $"declare query_parameters ({queryParameters});{publicQuery}",
                properties
            ))
            {
                while (result.Read())
                {
                    prTime = ((TimeSpan) result.GetValue(0)).TotalMinutes;
                }
            }

            using (ICslQueryProvider query =
                    _kustoClientProvider.GetKustoQueryConnectionProvider())
            using (IDataReader result = await query.ExecuteQueryAsync(
                _kustoClientProvider.GetKustoDatabase(),
                $"declare query_parameters ({queryParameters});{internalQuery}",
                properties
            ))
            {
                while (result.Read())
                {
                    officialTime = ((TimeSpan) result.GetValue(0)).TotalMinutes;
                }
            }

            return Ok(new BuildTime(id, officialTime, prTime));
        }
    }
}
