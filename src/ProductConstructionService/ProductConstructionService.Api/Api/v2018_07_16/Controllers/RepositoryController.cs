// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Immutable;
using System.ComponentModel.DataAnnotations;
using System.Net;
using Maestro.Data;
using Microsoft.AspNetCore.ApiPagination;
using Microsoft.AspNetCore.ApiVersioning;
using Microsoft.AspNetCore.ApiVersioning.Swashbuckle;
using Microsoft.AspNetCore.Mvc;
using Microsoft.DotNet.Services.Utility;
using ProductConstructionService.Api.v2018_07_16.Models;
using ProductConstructionService.WorkItems;

namespace ProductConstructionService.Api.Api.v2018_07_16.Controllers;

/// <summary>
///   Exposes methods to Create/Read/Update repository configuration information
/// </summary>
[Route("repo-config")]
[ApiVersion("2018-07-16")]
public class RepositoryController : ControllerBase
{
    public RepositoryController(
        BuildAssetRegistryContext context,
        IWorkItemProducerFactory workItemProducerFactory)
    {
        _context = context;
    }

    private BuildAssetRegistryContext _context { get; }

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
    public IActionResult ListRepositories(string? repository = null, string? branch = null)
    {
        IQueryable<Maestro.Data.Models.RepositoryBranch> query = _context.RepositoryBranches;

        if (!string.IsNullOrEmpty(repository))
        {
            query = query.Where(r => r.RepositoryName == repository);
        }

        if (!string.IsNullOrEmpty(branch))
        {
            var normalizedBranchName = GitHelpers.NormalizeBranchName(branch);
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
    public async Task<IActionResult> GetMergePolicies([Required] string repository, [Required] string branch)
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

        Maestro.Data.Models.RepositoryBranch? repoBranch = await _context.RepositoryBranches.FindAsync(repository, branch);
        if (repoBranch == null)
        {
            return NotFound();
        }

        List<Maestro.Data.Models.MergePolicyDefinition> policies =
            repoBranch.PolicyObject?.MergePolicies ?? [];
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

        Maestro.Data.Models.RepositoryBranch repoBranch = await GetRepositoryBranch(repository, branch);
        Maestro.Data.Models.RepositoryBranch.Policy policy = repoBranch.PolicyObject ?? new Maestro.Data.Models.RepositoryBranch.Policy();
        policy.MergePolicies = policies?.Select(p => p.ToDb()).ToList() ?? [];
        repoBranch.PolicyObject = policy;
        await _context.SaveChangesAsync();
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
    public async Task<IActionResult> GetHistory([Required] string repository, [Required] string branch)
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

        Maestro.Data.Models.RepositoryBranch? repoBranch = await _context.RepositoryBranches.FindAsync(repository, branch);

        if (repoBranch == null)
        {
            return NotFound();
        }

        IOrderedQueryable<RepositoryBranchUpdateHistoryEntry> query = _context.RepositoryBranchUpdateHistory
            .Where(u => u.Repository == repository && u.Branch == branch)
            .OrderByDescending(u => u.Timestamp);

        return Ok(query);
    }

    private async Task<Maestro.Data.Models.RepositoryBranch> GetRepositoryBranch(string repository, string branch)
    {
        Maestro.Data.Models.RepositoryBranch? repoBranch = await _context.RepositoryBranches.FindAsync(repository, branch);
        if (repoBranch == null)
        {
            _context.RepositoryBranches.Add(
                repoBranch = new Maestro.Data.Models.RepositoryBranch
                {
                    RepositoryName = repository,
                    BranchName = branch
                });
        }
        else
        {
            _context.RepositoryBranches.Update(repoBranch);
        }

        return repoBranch;
    }
}
