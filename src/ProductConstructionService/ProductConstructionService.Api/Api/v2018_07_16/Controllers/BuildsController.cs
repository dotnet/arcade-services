// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Net;
using Maestro.Data;
using Microsoft.AspNetCore.ApiPagination;
using Microsoft.AspNetCore.ApiVersioning;
using Microsoft.AspNetCore.ApiVersioning.Swashbuckle;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProductConstructionService.Api.v2018_07_16.Models;

namespace ProductConstructionService.Api.Api.v2018_07_16.Controllers;

/// <summary>
///   Exposes methods to Read/Query/Create <see cref="Build"/>s.
/// </summary>
[Route("builds")]
[ApiVersion("2018-07-16")]
public class BuildsController : ControllerBase
{
    protected readonly BuildAssetRegistryContext _context;

    public BuildsController(BuildAssetRegistryContext context)
    {
        _context = context;
    }

    /// <summary>
    ///   Gets a paged list of all <see cref="Build"/>s that match the given search criteria.
    /// </summary>
    /// <param name="repository">Repository</param>
    /// <param name="commit">Commit</param>
    /// <param name="buildNumber">Build number</param>
    /// <param name="channelId">Id of the channel to which the build applies</param>
    /// <param name="azdoAccount">Name of the Azure DevOps account</param>
    /// <param name="azdoBuildId">ID of the Azure DevOps build</param>
    /// <param name="azdoProject">Name of the Azure DevOps project</param>
    /// <param name="notBefore">Don't return <see cref="Build"/>s that happened before this time.</param>
    /// <param name="notAfter">Don't return <see cref="Build"/>s that happened after this time.</param>
    /// <param name="loadCollections">**true** to include the <see cref="Channel"/>, <see cref="Asset"/>, and dependent <see cref="Build"/> data with the response; **false** otherwise.</param>
    [HttpGet]
    [SwaggerApiResponse(HttpStatusCode.OK, Type = typeof(List<Build>), Description = "The list of Builds matching the search criteria")]
    [Paginated(typeof(Build))]
    [ValidateModelState]
    public virtual IActionResult ListBuilds(
        string? repository,
        string? commit,
        string? buildNumber,
        int? azdoBuildId,
        string? azdoAccount,
        string? azdoProject,
        int? channelId,
        DateTimeOffset? notBefore,
        DateTimeOffset? notAfter,
        bool? loadCollections)
    {
        IQueryable<Maestro.Data.Models.Build> query = Query(
            repository,
            commit,
            buildNumber,
            azdoBuildId,
            azdoAccount,
            azdoProject,
            channelId,
            notBefore,
            notAfter,
            loadCollections);
        return Ok(query);
    }

    protected IQueryable<Maestro.Data.Models.Build> Query(
        string? repository,
        string? commit,
        string? buildNumber,
        int? azdoBuildId,
        string? azdoAccount,
        string? azdoProject,
        int? channelId,
        DateTimeOffset? notBefore,
        DateTimeOffset? notAfter,
        bool? loadCollections)
    {
        IQueryable<Maestro.Data.Models.Build> query = _context.Builds;
        if (!string.IsNullOrEmpty(repository))
        {
            query = query.Where(b => repository == b.GitHubRepository || repository == b.AzureDevOpsRepository);
        }

        if (!string.IsNullOrEmpty(commit))
        {
            query = query.Where(b => b.Commit == commit);
        }

        if (!string.IsNullOrEmpty(buildNumber))
        {
            query = query.Where(b => b.AzureDevOpsBuildNumber == buildNumber);
        }

        if (azdoBuildId.HasValue)
        {
            query = query.Where(b => b.AzureDevOpsBuildId == azdoBuildId);
        }

        if (!string.IsNullOrEmpty(azdoAccount))
        {
            query = query.Where(b => b.AzureDevOpsAccount == azdoAccount);
        }

        if (!string.IsNullOrEmpty(azdoProject))
        {
            query = query.Where(b => b.AzureDevOpsProject == azdoProject);
        }

        if (notBefore.HasValue)
        {
            query = query.Where(b => b.DateProduced >= notBefore.Value);
        }

        if (notAfter.HasValue)
        {
            query = query.Where(b => b.DateProduced <= notAfter.Value);
        }

        if (channelId.HasValue)
        {
            query = query.Where(b => b.BuildChannels.Any(c => c.ChannelId == channelId.Value));
        }

        if (loadCollections ?? false)
        {
            query = query
                .Include(b => b.BuildChannels)
                .ThenInclude(bc => bc.Channel)
                .Include(b => b.Assets);
        }

        return query.OrderByDescending(b => b.DateProduced);
    }

    /// <summary>
    ///   Gets a single <see cref="Build"/>, including all the <see cref="Channel"/>, <see cref="Asset"/>, and dependent <see cref="Build"/> data.
    /// </summary>
    /// <param name="id">The id of the <see cref="Build"/>.</param>
    [HttpGet("{id}")]
    [SwaggerApiResponse(HttpStatusCode.OK, Type = typeof(Build), Description = "The requested Build")]
    [ValidateModelState]
    public virtual async Task<IActionResult> GetBuild(int id)
    {
        Maestro.Data.Models.Build? build = await _context.Builds.Where(b => b.Id == id)
            .Include(b => b.BuildChannels)
            .ThenInclude(bc => bc.Channel)
            .Include(b => b.Assets)
            .FirstOrDefaultAsync();

        if (build == null)
        {
            return NotFound();
        }

        return Ok(new Build(build));
    }

    /// <summary>
    ///   Gets the latest <see cref="Build"/>s that matches the given search criteria.
    /// </summary>
    /// <param name="repository">Filter by repository</param>
    /// <param name="commit">Filter by source commit</param>
    /// <param name="buildNumber">Filter by build</param>
    /// <param name="channelId">Filter by channel</param>
    /// <param name="notBefore">Don't return <see cref="Build"/>s that happened before this time.</param>
    /// <param name="notAfter">Don't return <see cref="Build"/>s that happened after this time.</param>
    /// <param name="loadCollections">**true** to include the <see cref="Channel"/>, <see cref="Asset"/>, and dependent <see cref="Build"/> data with the response; **false** otherwise.</param>
    [HttpGet("latest")]
    [SwaggerApiResponse(HttpStatusCode.OK, Type = typeof(Build), Description = "The latest Build matching the search criteria")]
    [ValidateModelState]
    public virtual async Task<IActionResult> GetLatest(
        string? repository,
        string? commit,
        string? buildNumber,
        int? channelId,
        DateTimeOffset? notBefore,
        DateTimeOffset? notAfter,
        bool? loadCollections)
    {
        IQueryable<Maestro.Data.Models.Build> query = Query(
            repository,
            commit,
            buildNumber,
            null,
            null,
            null,
            channelId,
            notBefore,
            notAfter,
            loadCollections);
        Maestro.Data.Models.Build? build = await query.OrderByDescending(o => o.DateProduced).FirstOrDefaultAsync();
        if (build == null)
        {
            return NotFound();
        }

        return Ok(new Build(build));
    }

    /// <summary>
    ///   Creates a new <see cref="Build"/> in the database.
    /// </summary>
    /// <param name="build">An object containing the data for the new <see cref="Build"/></param>
    [HttpPost]
    [SwaggerApiResponse(HttpStatusCode.Created, Type = typeof(Build), Description = "The created build")]
    [ValidateModelState]
    public virtual async Task<IActionResult> Create([FromBody] BuildData build)
    {
        Maestro.Data.Models.Build buildModel = build.ToDb();
        buildModel.DateProduced = DateTimeOffset.UtcNow;
        if (build.Dependencies?.Count > 0)
        {
            return BadRequest("This api version doesn't support build dependencies.");
        }
        await _context.Builds.AddAsync(buildModel);
        await _context.SaveChangesAsync();
        return CreatedAtRoute(
            new
            {
                action = "GetBuild",
                id = buildModel.Id
            },
            new Build(buildModel));
    }
}
