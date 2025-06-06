// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.ComponentModel.DataAnnotations;
using System.Net;
using Maestro.Data;
using Maestro.Data.Models;
using Microsoft.AspNetCore.ApiVersioning;
using Microsoft.AspNetCore.ApiVersioning.Swashbuckle;
using Microsoft.AspNetCore.Mvc;
using Microsoft.DotNet.DarcLib;
using Microsoft.EntityFrameworkCore;

using Build = Maestro.Data.Models.Build;
using Channel = ProductConstructionService.Api.v2020_02_20.Models.Channel;

namespace ProductConstructionService.Api.Api.v2020_02_20.Controllers;

/// <summary>
///   Exposes methods to Create/Read/Edit/Delete <see cref="Channel"/>s.
/// </summary>
/// <remarks>
///   Note that below there are several implementations of the channels controller that are overridden
///   from the base class, yet their implementations are identical. This is becaue the <see cref="Channel"/> type varies
///   between the v2018_07_16 and v2020_02_20 APIs, specifically around the removal of the ReleasePipelines
///   member. Don't remove them.
/// </remarks>
/// <seealso cref="v2018_07_16.Controllers.ChannelsController"/>
[Route("channels")]
[ApiVersion("2020-02-20")]
public class ChannelsController : v2018_07_16.Controllers.ChannelsController
{
    private readonly BuildAssetRegistryContext _context;

    public ChannelsController(
        BuildAssetRegistryContext context,
        IBasicBarClient barClient)
        : base(context, barClient)
    {
        _context = context;
    }

    [HttpGet]
    [SwaggerApiResponse(HttpStatusCode.OK, Type = typeof(List<Channel>), Description = "The list of Channels")]
    [ValidateModelState]
    public override IActionResult ListChannels(string? classification = null)
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
    public override async Task<IActionResult> ListRepositories(int id, int? withBuildsInDays = null)
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
    ///   Gets a single <see cref="Channel"/>.
    /// </summary>
    /// <param name="id">The id of the <see cref="Channel"/> to get</param>
    [HttpGet("{id}")]
    [SwaggerApiResponse(HttpStatusCode.OK, Type = typeof(Channel), Description = "The requested Channel")]
    [ValidateModelState]
    public override async Task<IActionResult> GetChannel(int id)
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
    ///   Deletes a <see cref="Channel"/>.
    /// </summary>
    /// <param name="id">The id of the <see cref="Channel"/> to delete</param>
    [HttpDelete("{id}")]
    [SwaggerApiResponse(HttpStatusCode.OK, Type = typeof(Channel), Description = "The Channel has been deleted")]
    [ValidateModelState]
    public override async Task<IActionResult> DeleteChannel(int id)
    {
        Maestro.Data.Models.Channel? channel = await _context.Channels
            .FirstOrDefaultAsync(c => c.Id == id);

        if (channel == null)
        {
            return NotFound();
        }

        // Ensure that there are no subscriptions associated with the channel
        if (await _context.Subscriptions.AnyAsync(s => s.ChannelId == id))
        {
            return BadRequest(
                new ApiError($"The channel with id '{id}' has associated subscriptions. " +
                             "Please remove these before removing this channel."));
        }

        _context.Channels.Remove(channel);

        await _context.SaveChangesAsync();
        return Ok(new Channel(channel));
    }

    /// <summary>
    ///   Creates a <see cref="Channel"/>.
    /// </summary>
    /// <param name="name">The name of the new <see cref="Channel"/>. This is required to be unique.</param>
    /// <param name="classification">The classification of the new <see cref="Channel"/></param>
    [HttpPost]
    [SwaggerApiResponse(HttpStatusCode.Created, Type = typeof(Channel), Description = "The Channel has been created")]
    [SwaggerApiResponse(HttpStatusCode.Conflict, Description = "A Channel with that name already exists.")]
    [HandleDuplicateKeyRows("Could not create channel '{name}'. A channel with the specified name already exists.")]
    public override async Task<IActionResult> CreateChannel([Required] string name, [Required] string classification)
    {
        var channelModel = new Maestro.Data.Models.Channel
        {
            Name = name,
            Classification = classification
        };
        await _context.Channels.AddAsync(channelModel);
        await _context.SaveChangesAsync();
        return CreatedAtRoute(
            new
            {
                action = "GetChannel",
                id = channelModel.Id
            },
            new Channel(channelModel));
    }

    /// <summary>
    ///   Adds an existing <see cref="Build"/> to the specified <see cref="Channel"/>
    /// </summary>
    /// <param name="channelId">The id of the <see cref="Channel"/>.</param>
    /// <param name="buildId">The id of the <see cref="Build"/></param>
    [HttpPost("{channelId}/builds/{buildId}")]
    [SwaggerApiResponse(HttpStatusCode.Created, Description = "Build successfully added to the Channel")]
    [HandleDuplicateKeyRows("Build {buildId} is already in channel {channelId}")]
    public override async Task<IActionResult> AddBuildToChannel(int channelId, int buildId)
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
    public override async Task<IActionResult> RemoveBuildFromChannel(int channelId, int buildId)
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
    ///   Update metadata of a <see cref="Channel"/>.
    /// </summary>
    /// <param name="id">The id of the <see cref="Channel"/> to update</param>
    /// <param name="name">Optional new name of the <see cref="Channel"/></param>
    /// <param name="classification">Optional new classification of the <see cref="Channel"/></param>
    [HttpPatch("{id}")]
    [SwaggerApiResponse(HttpStatusCode.OK, Type = typeof(Channel), Description = "The Channel has been updated")]
    [SwaggerApiResponse(HttpStatusCode.BadRequest, Description = "At least one of name or classification must be specified")]
    [SwaggerApiResponse(HttpStatusCode.Conflict, Description = "A Channel with that name already exists")]
    [HandleDuplicateKeyRows("Could not update channel. A channel with the specified name already exists.")]
    public override async Task<IActionResult> UpdateChannel(int id, string? name = null, string? classification = null)
    {
        return await base.UpdateChannel(id, name, classification);
    }

    [ApiRemoved]
    public override Task<IActionResult> AddPipelineToChannel(int channelId, int pipelineId)
    {
        throw new NotImplementedException();
    }

    [ApiRemoved]
    public override Task<IActionResult> DeletePipelineFromChannel(int channelId, int pipelineId)
    {
        throw new NotImplementedException();
    }
}
