// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Net;
using System.Text.RegularExpressions;
using Maestro.Data;
using Microsoft.AspNetCore.ApiVersioning;
using Microsoft.AspNetCore.ApiVersioning.Swashbuckle;
using Microsoft.AspNetCore.Mvc;
using Microsoft.DotNet.Services.Utility;
using Microsoft.EntityFrameworkCore;
using ProductConstructionService.Api.v2020_02_20.Models;

namespace ProductConstructionService.Api.Api.v2020_02_20.Controllers;

/// <summary>
///   Exposes methods to Read <see cref="DefaultChannel"/> mapping information.
/// </summary>
[Route("default-channels")]
[ApiVersion("2020-02-20")]
public class DefaultChannelsController : v2018_07_16.Controllers.DefaultChannelsController
{
    private readonly BuildAssetRegistryContext _context;

    // Branch names can't possibly start with -, so we'll use this fact to guarantee the user 
    // wants to use a regex, and not direct matching.
    private const string RegexBranchPrefix = "-regex:";

    public DefaultChannelsController(BuildAssetRegistryContext context)
        : base(context)
    {
        _context = context;
    }

    /// <summary>
    ///   Gets a list of all <see cref="DefaultChannel"/> mappings that match the given search criteria.
    /// </summary>
    /// <param name="repository"></param>
    /// <param name="branch"></param>
    /// <param name="channelId"></param>
    /// <param name="enabled">True if the default channel should be initially enabled or disabled.</param>
    [HttpGet]
    [SwaggerApiResponse(HttpStatusCode.OK, Type = typeof(List<DefaultChannel>), Description = "The list of DefaultChannels")]
    public override IActionResult List(string? repository = null, string? branch = null, int? channelId = null, bool? enabled = null)
    {
        IQueryable<Maestro.Data.Models.DefaultChannel> query = _context.DefaultChannels.Include(dc => dc.Channel)
            .AsNoTracking();

        if (!string.IsNullOrEmpty(repository))
        {
            query = query.Where(dc => dc.Repository == repository);
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

        if (!string.IsNullOrEmpty(branch))
        {
            List<DefaultChannel> branchFilteredResults = [];
            foreach (DefaultChannel defaultChannel in results)
            {
                // Branch name expressed as a regular expression: must start with '-regex:' and have at least one more character.
                // - Skips NormalizeBranchName here because internally everything is stored without that.
                //   If there's a pattern of users doing '-regex:/refs/heads/release.*' this could be revisited.
                if (defaultChannel.Branch.StartsWith(RegexBranchPrefix, StringComparison.InvariantCultureIgnoreCase) &&
                    defaultChannel.Branch.Length > RegexBranchPrefix.Length &&
                    new Regex(defaultChannel.Branch.Substring(RegexBranchPrefix.Length)).IsMatch(branch))
                {
                    branchFilteredResults.Add(defaultChannel);
                }
                else if (defaultChannel.Branch == GitHelpers.NormalizeBranchName(branch))
                {
                    branchFilteredResults.Add(defaultChannel);
                }
            }
            return Ok(branchFilteredResults);
        }
        return Ok(results);
    }

    /// <summary>
    ///   Gets a single <see cref="DefaultChannel"/>.
    /// </summary>
    /// <param name="id">The id of the <see cref="DefaultChannel"/></param>
    [HttpGet("{id}")]
    [SwaggerApiResponse(HttpStatusCode.OK, Type = typeof(DefaultChannel), Description = "The requested DefaultChannel")]
    [ValidateModelState]
    public override async Task<IActionResult> Get(int id)
    {
        Maestro.Data.Models.DefaultChannel? defaultChannel = await _context.DefaultChannels.FindAsync(id);
        if (defaultChannel == null)
        {
            return NotFound();
        }

        return Ok(new DefaultChannel(defaultChannel));
    }

}
