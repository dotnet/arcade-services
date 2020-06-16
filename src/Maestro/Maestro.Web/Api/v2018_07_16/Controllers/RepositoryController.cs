// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Maestro.Contracts;
using Maestro.Data;
using Maestro.Web.Api.v2018_07_16.Models;
using Microsoft.AspNetCore.ApiPagination;
using Microsoft.AspNetCore.ApiVersioning;
using Microsoft.AspNetCore.ApiVersioning.Swashbuckle;
using Microsoft.AspNetCore.Mvc;
using Microsoft.DotNet.Services.Utility;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Microsoft.DotNet.ServiceFabric.ServiceHost;
using Newtonsoft.Json.Linq;

namespace Maestro.Web.Api.v2018_07_16.Controllers
{
    /// <summary>
    ///   Exposes methods to Create/Read/Update repository configuration information
    /// </summary>
    [Route("repo-config")]
    [ApiVersion("2018-07-16")]
    public class RepositoryController : Controller
    {
        public RepositoryController(
            BuildAssetRegistryContext context,
            BackgroundQueue queue,
            IActorProxyFactory<IPullRequestActor> pullRequestActorFactory)
        {
            Context = context;
            Queue = queue;
            PullRequestActorFactory = pullRequestActorFactory;
        }

        public BuildAssetRegistryContext Context { get; }
        public BackgroundQueue Queue { get; }
        public IActorProxyFactory<IPullRequestActor> PullRequestActorFactory { get; }

        /// <summary>
        ///   Gets the list of <see cref="RepositoryBranch">RepositoryBranch</see>, optionally filtered by
        ///   repository and branch.
        /// </summary>
        /// <param name="repository">The repository</param>
        /// <param name="branch">The branch</param>
        [HttpGet("repositories")]
        [SwaggerApiResponse(HttpStatusCode.OK, Type = typeof(IList<RepositoryBranch>),
            Description = "The list of repositories+branches and their merge policies")]
        [ValidateModelState]
        public IActionResult ListRepositories(string repository = null, string branch = null)
        {
            IQueryable<Data.Models.RepositoryBranch> query = Context.RepositoryBranches;

            if (!string.IsNullOrEmpty(repository))
            {
                query = query.Where(r => r.RepositoryName == repository);
            }

            if (!string.IsNullOrEmpty(branch))
            {
                string normalizedBranchName = GitHelpers.NormalizeBranchName(branch);
                query = query.Where(r => r.BranchName == normalizedBranchName);
            }

            return Ok(query.AsEnumerable().Select(r => new RepositoryBranch(r)).ToList());
        }

        /// <summary>
        ///   Gets the list of <see cref="MergePolicy">MergePolicies</see> set up for the given repository and branch.
        /// </summary>
        /// <param name="repository">The repository</param>
        /// <param name="branch">The branch</param>
        [HttpGet("merge-policy")]
        [SwaggerApiResponse(HttpStatusCode.OK, Type = typeof(IList<MergePolicy>), Description = "The list of MergePolicies")]
        public async Task<IActionResult> GetMergePolicies([Required]string repository, [Required]string branch)
        {
            if (string.IsNullOrEmpty(repository))
            {
                ModelState.TryAddModelError(nameof(repository), "The repository parameter is required");
            }

            if (string.IsNullOrEmpty(branch))
            {
                ModelState.TryAddModelError(nameof(branch), "The branch parameter is required");
            }

            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            Data.Models.RepositoryBranch repoBranch = await Context.RepositoryBranches.FindAsync(repository, branch);
            if (repoBranch == null)
            {
                return NotFound();
            }

            List<Data.Models.MergePolicyDefinition> policies =
                repoBranch.PolicyObject?.MergePolicies ?? new List<Data.Models.MergePolicyDefinition>();
            return Ok(policies.Select(p => new MergePolicy(p)));
        }

        /// <summary>
        ///   Sets the <see cref="MergePolicy">MergePolicies</see> for the given repository and branch
        /// </summary>
        /// <param name="repository">The repository</param>
        /// <param name="branch">The branch</param>
        /// <param name="policies">The <see cref="MergePolicy">MergePolicies</see></param>
        [HttpPost("merge-policy")]
        [SwaggerApiResponse(HttpStatusCode.OK, Description = "MergePolicies successfully updated")]
        public async Task<IActionResult> SetMergePolicies(
            [Required] string repository,
            [Required] string branch,
            [FromBody] IImmutableList<MergePolicy> policies)
        {
            if (string.IsNullOrEmpty(repository))
            {
                ModelState.TryAddModelError(nameof(repository), "The repository parameter is required");
            }

            if (string.IsNullOrEmpty(branch))
            {
                ModelState.TryAddModelError(nameof(branch), "The branch parameter is required");
            }

            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            Data.Models.RepositoryBranch repoBranch = await GetRepositoryBranch(repository, branch);
            Data.Models.RepositoryBranch.Policy policy = repoBranch.PolicyObject ?? new Data.Models.RepositoryBranch.Policy();
            policy.MergePolicies = policies?.Select(p => p.ToDb()).ToList() ?? new List<Data.Models.MergePolicyDefinition>();
            repoBranch.PolicyObject = policy;
            await Context.SaveChangesAsync();
            return Ok();
        }

        /// <summary>
        ///   Gets a paginated list of the repository history for the given repository and branch
        /// </summary>
        /// <param name="repository">The repository</param>
        /// <param name="branch">The branch</param>
        [HttpGet("history")]
        [SwaggerApiResponse(HttpStatusCode.OK, Type = typeof(List<RepositoryHistoryItem>), Description = "The requested history")]
        [Paginated(typeof(RepositoryHistoryItem))]
        public async Task<IActionResult> GetHistory(string repository, string branch)
        {
            if (string.IsNullOrEmpty(repository))
            {
                ModelState.TryAddModelError(nameof(repository), "The repository parameter is required");
            }

            if (string.IsNullOrEmpty(branch))
            {
                ModelState.TryAddModelError(nameof(branch), "The branch parameter is required");
            }

            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            Data.Models.RepositoryBranch repoBranch = await Context.RepositoryBranches.FindAsync(repository, branch);

            if (repoBranch == null)
            {
                return NotFound();
            }

            IOrderedQueryable<RepositoryBranchUpdateHistoryEntry> query = Context.RepositoryBranchUpdateHistory
                .Where(u => u.Repository == repository && u.Branch == branch)
                .OrderByDescending(u => u.Timestamp);

            return Ok(query);
        }

        /// <summary>
        ///   Requests that Maestro++ retry the referenced history item.
        ///   Links to this api are returned from the <see cref="GetHistory"/> api.
        /// </summary>
        /// <param name="repository">The repository</param>
        /// <param name="branch">The branch</param>
        /// <param name="timestamp">The timestamp identifying the history item to retry</param>
        [HttpPost("retry/{timestamp}")]
        [SwaggerApiResponse(HttpStatusCode.Accepted, Description = "Retry successfully requested")]
        [SwaggerApiResponse(HttpStatusCode.NotAcceptable, Description = "The requested history item was successful and cannot be retried")]
        public async Task<IActionResult> RetryActionAsync([Required] string repository, [Required] string branch, long timestamp)
        {
            if (string.IsNullOrEmpty(repository))
            {
                ModelState.TryAddModelError(nameof(repository), "The repository parameter is required");
            }

            if (string.IsNullOrEmpty(branch))
            {
                ModelState.TryAddModelError(nameof(branch), "The branch parameter is required");
            }

            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            DateTime ts = DateTimeOffset.FromUnixTimeSeconds(timestamp).UtcDateTime;

            Data.Models.RepositoryBranch repoBranch = await Context.RepositoryBranches.FindAsync(repository, branch);

            if (repoBranch == null)
            {
                return NotFound();
            }

            RepositoryBranchUpdateHistoryEntry update = await Context.RepositoryBranchUpdateHistory
                .Where(u => u.Repository == repository && u.Branch == branch)
                .FirstOrDefaultAsync(u => Math.Abs(EF.Functions.DateDiffSecond(u.Timestamp, ts)) < 1);

            if (update == null)
            {
                return NotFound();
            }

            if (update.Success)
            {
                return StatusCode(
                    (int) HttpStatusCode.NotAcceptable,
                    new ApiError("That action was successful, it cannot be retried."));
            }

            Queue.Post<PullRequestActionWorkItem>(PullRequestActionWorkItem.GetArguments(update));

            return Accepted();
        }

        private class PullRequestActionWorkItem : IBackgroundWorkItem
        {
            private readonly IActorProxyFactory<IPullRequestActor> _factory;

            public PullRequestActionWorkItem(IActorProxyFactory<IPullRequestActor> factory)
            {
                this._factory = factory;
            }

            public Task ProcessAsync(JToken argumentToken)
            {
                var update = argumentToken.ToObject<Arguments>();
                IPullRequestActor actor = _factory.Lookup(PullRequestActorId.Create(update.Repository, update.Branch));
                return actor.RunActionAsync(update.Method, update.MethodArguments);
            }

            public static JToken GetArguments(RepositoryBranchUpdateHistoryEntry update)
            {
                return JToken.FromObject(new Arguments {Repository = update.Repository, Branch = update.Branch, Method = update.Method, MethodArguments = update.Arguments});
            }

            private struct Arguments
            {
                public string Repository;
                public string Branch;
                public string Method;
                public string MethodArguments;
            }
        }

        private async Task<Data.Models.RepositoryBranch> GetRepositoryBranch(string repository, string branch)
        {
            Data.Models.RepositoryBranch repoBranch = await Context.RepositoryBranches.FindAsync(repository, branch);
            if (repoBranch == null)
            {
                Context.RepositoryBranches.Add(
                    repoBranch = new Data.Models.RepositoryBranch
                    {
                        RepositoryName = repository,
                        BranchName = branch
                    });
            }
            else
            {
                Context.RepositoryBranches.Update(repoBranch);
            }

            return repoBranch;
        }
    }
}
