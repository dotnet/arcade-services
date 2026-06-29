// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Net;
using Maestro.Data;
using Maestro.Data.Models;
using Microsoft.AspNetCore.ApiVersioning;
using Microsoft.AspNetCore.ApiVersioning.Swashbuckle;
using Microsoft.AspNetCore.Mvc;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Models.Darc;
using Microsoft.EntityFrameworkCore;
using Build = Maestro.Data.Models.Build;
using Channel = ProductConstructionService.Api.v2018_07_16.Models.Channel;
using FlowGraph = ProductConstructionService.Api.v2018_07_16.Models.FlowGraph;
using ReleasePipeline = ProductConstructionService.Api.v2018_07_16.Models.ReleasePipeline;

namespace ProductConstructionService.Api.Api.v2018_07_16.Controllers;

/// <summary>
///   Exposes methods to Read <see cref="Channel"/>s.
/// </summary>
[Route("channels")]
[ApiVersion("2018-07-16")]
public class ChannelsController : ControllerBase
{
    private readonly BuildAssetRegistryContext _context;
    private readonly IBasicBarClient _barClient;

    public ChannelsController(
        BuildAssetRegistryContext context,
        IBasicBarClient barClient)
    {
        _context = context;
        _barClient = barClient;
    }

    /// <summary>
    ///   Gets a list of all <see cref="Channel"/>s that match the given classification.
    /// </summary>
    /// <param name="classification">The <see cref="Channel.Classification"/> of <see cref="Channel"/> to get</param>
    [HttpGet]
    [SwaggerApiResponse(HttpStatusCode.OK, Type = typeof(List<Channel>), Description = "The list of Channels")]
    [ValidateModelState]
    public virtual IActionResult ListChannels(string? classification = null)
    {
        IQueryable<Maestro.Data.Models.Channel> query = _context.Channels;
        if (!string.IsNullOrEmpty(classification))
        {
            query = query.Where(c => c.Classification == classification);
        }

        var results = query.AsEnumerable().Select(c => new Channel(c)).ToList();
        return Ok(results);
    }

    /// <summary>
    ///     Gets a list of repositories that have had builds applied to the specified channel.
    /// </summary>
    /// <param name="id">Channel id</param>
    /// <param name="withBuildsInDays">If specified, lists only repositories that have had builds assigned to the channel in the last N days. Must be > 0</param>
    /// <returns></returns>
    [HttpGet("{id}/repositories")]
    [SwaggerApiResponse(HttpStatusCode.OK, Type = typeof(List<string>), Description = "List of repositories in a Channel, optionally restricting to repositories with builds in last N days.")]
    [ValidateModelState]
    public virtual async Task<IActionResult> ListRepositories(int id, int? withBuildsInDays = null)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;

        var buildChannelList = _context.BuildChannels
            .Include(b => b.Build)
            .Where(bc => bc.ChannelId == id);

        if (withBuildsInDays != null)
        {
            if (withBuildsInDays <= 0)
            {
                return BadRequest(
                    new ApiError($"withBuildsInDays should be greater than 0."));
            }

            buildChannelList = buildChannelList
                .Where(bc => now.Subtract(bc.Build.DateProduced).TotalDays < withBuildsInDays);
        }

        List<string> repositoryList = await buildChannelList
            .Select(bc => bc.Build.GitHubRepository ?? bc.Build.AzureDevOpsRepository)
            .Where(b => !string.IsNullOrEmpty(b))
            .Distinct()
            .ToListAsync();

        return Ok(repositoryList);
    }

    /// <summary>
    ///   Gets a single <see cref="Channel"/>, including all <see cref="ReleasePipeline"/> data.
    /// </summary>
    /// <param name="id">The id of the <see cref="Channel"/> to get</param>
    [HttpGet("{id}")]
    [SwaggerApiResponse(HttpStatusCode.OK, Type = typeof(Channel), Description = "The requested Channel")]
    [ValidateModelState]
    public virtual async Task<IActionResult> GetChannel(int id)
    {
        Maestro.Data.Models.Channel? channel = await _context.Channels
            .Where(c => c.Id == id).FirstOrDefaultAsync();

        if (channel == null)
        {
            return NotFound();
        }

        return Ok(new Channel(channel));
    }

    /// <summary>
    ///   Adds an existing <see cref="Build"/> to the specified <see cref="Channel"/>
    /// </summary>
    /// <param name="channelId">The id of the <see cref="Channel"/>.</param>
    /// <param name="buildId">The id of the <see cref="Build"/></param>
    [HttpPost("{channelId}/builds/{buildId}")]
    [SwaggerApiResponse(HttpStatusCode.Created, Description = "Build successfully added to the Channel")]
    [HandleDuplicateKeyRows("Build {buildId} is already in channel {channelId}")]
    public virtual async Task<IActionResult> AddBuildToChannel(int channelId, int buildId)
    {
        Maestro.Data.Models.Channel? channel = await _context.Channels.FindAsync(channelId);
        if (channel == null)
        {
            return NotFound(new ApiError($"The channel with id '{channelId}' was not found."));
        }

        Build? build = await _context.Builds
            .Where(b => b.Id == buildId)
            .Include(b => b.BuildChannels)
            .FirstOrDefaultAsync();

        if (build == null)
        {
            return NotFound(new ApiError($"The build with id '{buildId}' was not found."));
        }

        // If build is already in channel, nothing to do
        if (build.BuildChannels.Any(existingBuildChannels => existingBuildChannels.ChannelId == channelId))
        {
            return StatusCode((int)HttpStatusCode.Created);
        }

        var buildChannel = new BuildChannel
        {
            Channel = channel,
            Build = build,
            DateTimeAdded = DateTimeOffset.UtcNow
        };
        await _context.BuildChannels.AddAsync(buildChannel);
        await _context.SaveChangesAsync();
        return StatusCode((int)HttpStatusCode.Created);
    }

    /// <summary>
    ///   Remove a build from a channel.
    /// </summary>
    /// <param name="channelId">The id of the <see cref="Channel"/>.</param>
    /// <param name="buildId">The id of the <see cref="Build"/></param>
    [HttpDelete("{channelId}/builds/{buildId}")]
    [SwaggerApiResponse(HttpStatusCode.OK, Description = "Build successfully removed from the Channel")]
    public virtual async Task<IActionResult> RemoveBuildFromChannel(int channelId, int buildId)
    {
        BuildChannel? buildChannel = await _context.BuildChannels
            .Where(bc => bc.BuildId == buildId && bc.ChannelId == channelId)
            .FirstOrDefaultAsync();

        if (buildChannel == null)
        {
            return StatusCode((int)HttpStatusCode.NotModified);
        }

        _context.BuildChannels.Remove(buildChannel);
        await _context.SaveChangesAsync();
        return StatusCode((int)HttpStatusCode.OK);
    }

    /// <summary>
    ///   Add an existing <see cref="ReleasePipeline"/> to the specified <see cref="Channel"/>
    /// </summary>
    /// <param name="channelId">The id of the <see cref="Channel"/></param>
    /// <param name="pipelineId">The id of the <see cref="ReleasePipeline"/></param>
    [HttpPost("{channelId}/pipelines/{pipelineId}")]
    [SwaggerApiResponse(HttpStatusCode.Created, Description = "ReleasePipeline successfully added to Channel")]
    public virtual async Task<IActionResult> AddPipelineToChannel(int channelId, int pipelineId)
    {
        return await Task.FromResult(StatusCode((int)HttpStatusCode.NotModified));
    }

    /// <summary>
    ///   Remove a <see cref="ReleasePipeline"/> from the specified <see cref="Channel"/>
    /// </summary>
    /// <param name="channelId">The id of the <see cref="Channel"/></param>
    /// <param name="pipelineId">The id of the <see cref="ReleasePipeline"/></param>
    [HttpDelete("{channelId}/pipelines/{pipelineId}")]
    [SwaggerApiResponse(HttpStatusCode.OK, Description = "ReleasePipelines successfully removed from Channel")]
    public virtual async Task<IActionResult> DeletePipelineFromChannel(int channelId, int pipelineId)
    {
        return await Task.FromResult(StatusCode((int)HttpStatusCode.NotModified));
    }

    /// <summary>
    ///   Get the dependency flow graph for the specified <see cref="Channel"/>
    /// </summary>
    /// <param name="channelId">The id of the <see cref="Channel"/></param>
    /// <param name="includeDisabledSubscriptions">Include disabled subscriptions</param>
    /// <param name="includedFrequencies">Frequencies to include</param>
    /// <param name="includeBuildTimes">If we should create the flow graph with build times</param>
    /// <param name="days">Number of days over which to summarize build times</param>
    /// <param name="includeArcade">If we should include arcade in the flow graph</param>
    [HttpGet("{channelId}/graph")]
    [SwaggerApiResponse(HttpStatusCode.OK, Type = typeof(FlowGraph), Description = "The dependency flow graph for a channel")]
    [ValidateModelState]
    public async Task<IActionResult> GetFlowGraphAsync(
        int channelId = 0,
        bool includeDisabledSubscriptions = false,
        IEnumerable<string>? includedFrequencies = default,
        bool includeBuildTimes = false,
        int days = 7,
        bool includeArcade = true)
    {
        DependencyFlowGraph flowGraph = await _barClient.GetDependencyFlowGraphAsync(
            channelId,
            days,
            includeArcade,
            includeBuildTimes,
            includeDisabledSubscriptions,
            includedFrequencies?.ToList());

        // Convert flow graph to correct return type
        return Ok(FlowGraph.Create(flowGraph));
    }
}
