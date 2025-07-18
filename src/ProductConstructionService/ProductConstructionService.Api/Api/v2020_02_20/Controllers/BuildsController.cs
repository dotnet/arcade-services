// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.ComponentModel.DataAnnotations;
using System.Net;
using ProductConstructionService.Api.v2020_02_20.Models;
using Maestro.Data;
using Microsoft.AspNetCore.ApiPagination;
using Microsoft.AspNetCore.ApiVersioning;
using Microsoft.AspNetCore.ApiVersioning.Swashbuckle;
using Microsoft.AspNetCore.Mvc;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Models.VirtualMonoRepo;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Internal;
using ProductConstructionService.DependencyFlow.WorkItems;
using ProductConstructionService.WorkItems;

namespace ProductConstructionService.Api.Api.v2020_02_20.Controllers;

/// <summary>
///   Exposes methods to Read/Query/Create <see cref="Build"/>s.
/// </summary>
[Route("builds")]
[ApiVersion("2020-02-20")]
public class BuildsController : v2019_01_16.Controllers.BuildsController
{
    private readonly IRemoteFactory _factory;
    private readonly IWorkItemProducerFactory _workItemProducerFactory;

    public BuildsController(
        BuildAssetRegistryContext context,
        ISystemClock clock,
        IRemoteFactory factory,
        IWorkItemProducerFactory workItemProducerFactory)
        : base(context, clock)
    {
        _factory = factory;
        _workItemProducerFactory = workItemProducerFactory;
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
    /// <param name="loadCollections">**true** to include the <see cref="ProductConstructionService.Api.v2018_07_16.Models.Channel"/>, <see cref="ProductConstructionService.Api.v2018_07_16.Models.Asset"/>, and dependent <see cref="Build"/> data with the response; **false** otherwise.</param>
    [HttpGet]
    [SwaggerApiResponse(HttpStatusCode.OK, Type = typeof(List<Build>), Description = "The list of Builds matching the search criteria")]
    [Paginated(typeof(Build))]
    [ValidateModelState]
    public override IActionResult ListBuilds(
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

    /// <summary>
    ///   Gets a single <see cref="Build"/>, including all the <see cref="ProductConstructionService.Api.v2018_07_16.Models.Channel"/>, <see cref="ProductConstructionService.Api.v2018_07_16.Models.Asset"/>, and dependent <see cref="Build"/> data.
    /// </summary>
    /// <param name="id">The id of the <see cref="Build"/>.</param>
    [HttpGet("{id}")]
    [SwaggerApiResponse(HttpStatusCode.OK, Type = typeof(Build), Description = "The requested Build")]
    [ValidateModelState]
    public override async Task<IActionResult> GetBuild(int id)
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

        List<Maestro.Data.Models.BuildDependency> dependentBuilds = [.. _context.BuildDependencies.Where(b => b.BuildId == id)];
        build.DependentBuildIds = dependentBuilds;

        return Ok(new Build(build));
    }

    [HttpGet("{id}/graph")]
    [SwaggerApiResponse(HttpStatusCode.OK, Type = typeof(BuildGraph), Description = "The tree of build dependencies")]
    [ValidateModelState]
    public override async Task<IActionResult> GetBuildGraph(int id)
    {
        Maestro.Data.Models.Build? build = await _context.Builds.Include(b => b.Incoherencies).FirstOrDefaultAsync(b => b.Id == id);

        if (build == null)
        {
            return NotFound();
        }

        var builds = await _context.GetBuildGraphAsync(build.Id);

        return Ok(BuildGraph.Create(builds.Select(b => new Build(b))));
    }

    /// <summary>
    ///   Gets the latest <see cref="Build"/>s that matches the given search criteria.
    /// </summary>
    /// <param name="repository">Repository</param>
    /// <param name="commit">Commit</param>
    /// <param name="buildNumber">Build number</param>
    /// <param name="channelId">Id of the channel to which the build applies</param>
    /// <param name="notBefore">Don't return <see cref="Build"/>s that happened before this time.</param>
    /// <param name="notAfter">Don't return <see cref="Build"/>s that happened after this time.</param>
    /// <param name="loadCollections">**true** to include the <see cref="ProductConstructionService.Api.v2018_07_16.Models.Channel"/>, <see cref="ProductConstructionService.Api.v2018_07_16.Models.Asset"/>, and dependent <see cref="Build"/> data with the response; **false** otherwise.</param>
    [HttpGet("latest")]
    [SwaggerApiResponse(HttpStatusCode.OK, Type = typeof(Build), Description = "The latest Build matching the search criteria")]
    [ValidateModelState]
    public override async Task<IActionResult> GetLatest(
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

    [HttpGet("{buildId}/commit")]
    [SwaggerApiResponse(HttpStatusCode.OK, Type = typeof(ProductConstructionService.Api.v2020_02_20.Models.Commit), Description = "The commit matching specified criteria")]
    [ValidateModelState]
    public async Task<IActionResult> GetCommit(int buildId)
    {
        Maestro.Data.Models.Build? build = await _context.Builds.Include(b => b.Incoherencies).FirstOrDefaultAsync(b => b.Id == buildId);
        if (build == null)
        {
            return NotFound();
        }

        IRemote remote = await _factory.CreateRemoteAsync(build.AzureDevOpsRepository ?? build.GitHubRepository);
        Microsoft.DotNet.DarcLib.Commit commit = await remote.GetCommitAsync(build.AzureDevOpsRepository ?? build.GitHubRepository, build.Commit);
        if (commit == null)
        {
            return NotFound();
        }

        return Ok(new ProductConstructionService.Api.v2020_02_20.Models.Commit(commit.Author, commit.Sha, commit.Message));
    }

    /// <summary>
    ///   Gets the source manifest for a VMR build at the specified commit.
    /// </summary>
    /// <param name="buildId">The id of the <see cref="Build"/>.</param>
    [HttpGet("{buildId}/source-manifest")]
    [SwaggerApiResponse(HttpStatusCode.OK, Type = typeof(List<SourceManifestEntry>), Description = "The source manifest at the build's commit")]
    [SwaggerApiResponse(HttpStatusCode.NotFound, Description = "Build not found or source manifest not available")]
    [ValidateModelState]
    public async Task<IActionResult> GetSourceManifest(int buildId)
    {
        Maestro.Data.Models.Build? build = await _context.Builds.FirstOrDefaultAsync(b => b.Id == buildId);
        if (build == null)
        {
            return NotFound();
        }

        string repository = build.GetRepository();
        if (string.IsNullOrEmpty(repository))
        {
            return NotFound("Repository information not available for this build");
        }

        try
        {
            IRemote remote = await _factory.CreateRemoteAsync(repository);
            SourceManifest sourceManifest = await remote.GetSourceManifestAtCommitAsync(repository, build.Commit);
            
            var entries = sourceManifest.Repositories
                .Select(r => new SourceManifestEntry(r.Path, r.RemoteUri, r.CommitSha, r.BarId))
                .OrderBy(e => e.Path)
                .ToList();
            
            return Ok(entries);
        }
        catch (Exception ex)
        {
            // Source manifest may not exist for non-VMR builds
            return NotFound($"Source manifest not found: {ex.Message}");
        }
    }

    [ApiRemoved]
    public sealed override Task<IActionResult> Update(int buildId, [FromBody, Required] ProductConstructionService.Api.v2019_01_16.Models.BuildUpdate buildUpdate)
    {
        throw new NotImplementedException();
    }

    [HttpPatch("{buildId}")]
    [SwaggerApiResponse(HttpStatusCode.OK, Type = typeof(Build), Description = "Update a build with new information.")]
    [ValidateModelState]
    public virtual async Task<IActionResult> Update(int buildId, [FromBody, Required] BuildUpdate buildUpdate)
    {
        Maestro.Data.Models.Build? build = await _context.Builds.Where(b => b.Id == buildId).FirstOrDefaultAsync();

        if (build == null)
        {
            return NotFound();
        }

        var doUpdate = false;
        if (buildUpdate.Released.HasValue && build.Released != buildUpdate.Released.Value)
        {
            build.Released = buildUpdate.Released.Value;
            doUpdate = true;
        }

        if (doUpdate)
        {
            _context.Builds.Update(build);
            await _context.SaveChangesAsync();
        }

        return Ok(new Build(build));
    }

    [ApiRemoved]
    public sealed override Task<IActionResult> Create(ProductConstructionService.Api.v2019_01_16.Models.BuildData build)
    {
        throw new NotImplementedException();
    }

    /// <summary>
    ///   Creates a new <see cref="Build"/> in the database.
    /// </summary>
    /// <param name="build">An object containing the data for the new <see cref="Build"/></param>
    [HttpPost]
    [SwaggerApiResponse(HttpStatusCode.Created, Type = typeof(Build), Description = "The created build")]
    [ValidateModelState]
    public async Task<IActionResult> Create([FromBody, Required] BuildData build)
    {
        Maestro.Data.Models.Build buildModel = build.ToDb();
        buildModel.DateProduced = DateTimeOffset.UtcNow;

        if (build.Dependencies != null)
        {
            // For each Dependency, update the time to Inclusion.
            // This measure is to be used for telemetry purposes, and has several known corner cases
            // where the measurement will not be correct:
            // 1. For any dependencies that were added before this column was added, the TimeToInclusionInMinutes
            //    will be 0.
            // 2. For new release branches, until new builds of dependencies are added, this will recalculate
            //    the TimeToInclusion, so it will seem inordinately large until new builds are added. This will
            //    be particularly true for dependencies that are infrequently updated.
            foreach (var dep in build.Dependencies)
            {
                // Heuristic to discover if this dependency has been added to the same repository and branch 
                // of the current build. If we find a match in the BuildDependencies table, it means
                // that this is not a new dependency, and we should use the TimeToInclusionInMinutes
                // of the previous time this dependency was added.
                var buildDependency = await _context.BuildDependencies.FirstOrDefaultAsync(d =>
                    d.DependentBuildId == dep.BuildId &&
                    d.Build.GitHubRepository == buildModel.GitHubRepository &&
                    d.Build.GitHubBranch == buildModel.GitHubBranch &&
                    d.Build.AzureDevOpsRepository == buildModel.AzureDevOpsRepository &&
                    d.Build.AzureDevOpsBranch == buildModel.AzureDevOpsBranch
                );

                if (buildDependency != null)
                {
                    dep.TimeToInclusionInMinutes = buildDependency.TimeToInclusionInMinutes;
                }
                else
                {
                    // If the dependent build is not currently in the BuildDependency table for this repo/branch (ie is a new dependency),
                    // find the dependency in the Builds table and calculate the time to inclusion

                    // We want to use the BuildChannel insert time if it exists. So we need to heuristically:
                    // 1. Find the subscription between these two repositories on the current branch
                    // 2. Find the entry in BuildChannels and get the insert time
                    // In certain corner cases, we may pick the wrong subscription or BuildChannel

                    Maestro.Data.Models.Build? depBuild = await _context.Builds.FindAsync(dep.BuildId);

                    if (depBuild == null)
                    {
                        return BadRequest($"Build {dep.BuildId} not found.");
                    }

                    // If we don't find a subscription or a BuildChannel entry, use the dependency's
                    // date produced.
                    DateTimeOffset startTime = depBuild.DateProduced;

                    Maestro.Data.Models.Subscription? subscription = await _context.Subscriptions.FirstOrDefaultAsync(s =>
                        (s.SourceRepository == depBuild.GitHubRepository ||
                         s.SourceRepository == depBuild.AzureDevOpsRepository) &&
                        (s.TargetRepository == buildModel.GitHubRepository ||
                         s.TargetRepository == buildModel.AzureDevOpsRepository) &&
                        (s.TargetBranch == buildModel.GitHubBranch ||
                         s.TargetBranch == buildModel.AzureDevOpsBranch));


                    if (subscription != null)
                    {
                        Maestro.Data.Models.BuildChannel? buildChannel = await _context.BuildChannels
                            .FirstOrDefaultAsync(bc => bc.BuildId == depBuild.Id && bc.ChannelId == subscription.ChannelId);

                        if (buildChannel != null)
                        {
                            startTime = buildChannel.DateTimeAdded;
                        }
                    }

                    dep.TimeToInclusionInMinutes = (buildModel.DateProduced - startTime).TotalMinutes;
                }
            }

            await _context.BuildDependencies.AddRangeAsync(
                build.Dependencies.Select(
                    b => new Maestro.Data.Models.BuildDependency
                    {
                        Build = buildModel,
                        DependentBuildId = b.BuildId,
                        IsProduct = b.IsProduct,
                        TimeToInclusionInMinutes = b.TimeToInclusionInMinutes,
                    }));
        }

        await _context.Builds.AddAsync(buildModel);
        await _context.SaveChangesAsync();

        // Compute the dependency incoherencies of the build.
        // Since this might be an expensive operation we do it asynchronously.
        await _workItemProducerFactory.CreateProducer<BuildCoherencyInfoWorkItem>()
            .ProduceWorkItemAsync(new()
            {
                BuildId = buildModel.Id
            });

        return CreatedAtRoute(
            new
            {
                action = "GetBuild",
                id = buildModel.Id
            },
            new Build(buildModel));
    }
}
