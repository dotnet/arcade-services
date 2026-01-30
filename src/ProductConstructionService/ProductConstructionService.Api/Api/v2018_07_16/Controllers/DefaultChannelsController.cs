// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Net;
using Maestro.Data;
using Microsoft.AspNetCore.ApiVersioning;
using Microsoft.AspNetCore.ApiVersioning.Swashbuckle;
using Microsoft.AspNetCore.Mvc;
using Microsoft.DotNet.Services.Utility;
using Microsoft.EntityFrameworkCore;
using ProductConstructionService.Api.v2018_07_16.Models;

namespace ProductConstructionService.Api.Api.v2018_07_16.Controllers;

/// <summary>
///   Exposes methods to Read <see cref="DefaultChannel"/> mapping information.
/// </summary>
[Route("default-channels")]
[ApiVersion("2018-07-16")]
public class DefaultChannelsController : ControllerBase
{
    private readonly BuildAssetRegistryContext _context;

    public DefaultChannelsController(BuildAssetRegistryContext context)
    {
        _context = context;
    }

    /// <summary>
    ///   Gets a list of all <see cref="DefaultChannel"/> mappings that match the given search criteria.
    /// </summary>
    /// <param name="repository">Filter by repository</param>
    /// <param name="channelId">Filter by channel</param>
    /// <param name="branch">Filter by branch</param>
    /// <param name="enabled">True if the default channel should be initially enabled or disabled.</param>
    [HttpGet]
    [SwaggerApiResponse(HttpStatusCode.OK, Type = typeof(List<DefaultChannel>), Description = "The list of DefaultChannels")]
    public virtual IActionResult List(string? repository = null, string? branch = null, int? channelId = null, bool? enabled = null)
    {
        IQueryable<Maestro.Data.Models.DefaultChannel> query = _context.DefaultChannels.Include(dc => dc.Channel)
            .AsNoTracking();

        if (!string.IsNullOrEmpty(repository))
        {
            query = query.Where(dc => dc.Repository == repository);
        }

        if (!string.IsNullOrEmpty(branch))
        {
            // Normalize the branch name to not include refs/heads
            var normalizedBranchName = GitHelpers.NormalizeBranchName(branch);
            query = query.Where(dc => dc.Branch == normalizedBranchName);
        }

        if (channelId.HasValue)
        {
            query = query.Where(dc => dc.ChannelId == channelId.Value);
        }

        if (enabled.HasValue)
        {
            query = query.Where(dc => dc.Enabled == enabled.Value);
        }

        List<DefaultChannel> results = [.. query.AsEnumerable().Select(dc => new DefaultChannel(dc))];
        return Ok(results);
    }

    /// <summary>
    ///   Gets a single <see cref="DefaultChannel"/>.
    /// </summary>
    /// <param name="id">The id of the <see cref="DefaultChannel"/></param>
    [HttpGet("{id}")]
    [SwaggerApiResponse(HttpStatusCode.OK, Type = typeof(DefaultChannel), Description = "The requested DefaultChannel")]
    [ValidateModelState]
    public virtual async Task<IActionResult> Get(int id)
    {
        Maestro.Data.Models.DefaultChannel? defaultChannel = await _context.DefaultChannels.FindAsync(id);
        if (defaultChannel == null)
        {
            return NotFound();
        }

        return Ok(new DefaultChannel(defaultChannel));
    }
}
