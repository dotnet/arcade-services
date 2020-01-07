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

            var parameters = new List<KustoParameter> {
                new KustoParameter("_Repository", KustoDataTypes.String,  defaultChannel.Repository.Split("/").Last()),
                new KustoParameter("_SourceBranch", KustoDataTypes.String, defaultChannel.Branch),
                new KustoParameter("_Days", KustoDataTypes.TimeSpan, $"{days}d")
            };

            // We only care about builds that complete successfully or partially successfully 
            // from the given repository. We summarize duration of the builds over the last 7 days.
            // There are multiple different definitions that run in parallel, so we summarize
            // on the definition id and ultimately choose the definition that took the longest.
            string commonQueryText = @"| where Repository endswith _Repository
                | where Result != 'failed' and Result != 'canceled' 
                | where FinishTime > ago(_Days) 
                | extend duration = FinishTime - StartTime 
                | summarize average_duration = avg(duration) by DefinitionId
                | summarize max(average_duration)";

            // We only want the pull request time from the public ci. We exclude on target branch,
            // as all PRs come in as refs/heads/#/merge rather than what branch they are trying to
            // apply to.
            string publicQueryText = $@"TimelineBuilds 
                | project Repository, SourceBranch, TargetBranch, DefinitionId, StartTime, FinishTime, Result, Project, Reason
                | where Project == 'public'
                | where Reason == 'pullRequest' 
                | where TargetBranch == _SourceBranch
                {commonQueryText}";

            // For the official build times, we want the builds that were generated as a CI run 
            // (either batchedCI or individualCI) for a specific branch--i.e. we want the builds
            // that are part of generating the product.
            string internalQueryText = $@"TimelineBuilds 
                | project Repository, SourceBranch, DefinitionId, StartTime, FinishTime, Result, Project, Reason
                | where Project == 'internal' 
                | where Reason == 'batchedCI' or Reason == 'individualCI'
                | where SourceBranch == _SourceBranch
                {commonQueryText}";

            KustoQuery internalQuery = new KustoQuery(internalQueryText, parameters);
            KustoQuery publicQuery = new KustoQuery(publicQueryText, parameters);

            var results = await Task.WhenAll<TimeSpan>(_kustoClientProvider.GetSingleValueFromQueryAsync<TimeSpan>(internalQuery), 
                _kustoClientProvider.GetSingleValueFromQueryAsync<TimeSpan>(publicQuery));

            double officialTime = results[0].TotalMinutes;
            double prTime = results[1].TotalMinutes;

            return Ok(new BuildTime(id, officialTime, prTime));
        }
    }
}
