// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;

namespace Maestro.Web.Api.v2020_02_20.Models;

public class SubscriptionData
{
    [Required]
    public string ChannelName { get; set; }

    [Required]
    public string SourceRepository { get; set; }

    [Required]
    [RepositoryUrl]
    public string TargetRepository { get; set; }

    [Required]
    public string TargetBranch { get; set; }

    public bool? Enabled { get; set; }

    public bool? SourceEnabled { get; set; }

    public string SourceDirectory { get; set; }

    public string TargetDirectory { get; set; }

    [Required]
    public v2018_07_16.Models.SubscriptionPolicy Policy { get; set; }

    public string PullRequestFailureNotificationTags { get; set; }

    public IReadOnlyCollection<string> ExcludedAssets { get; set; }

    public Data.Models.Subscription ToDb() => new()
    {
        SourceRepository = SourceRepository,
        TargetRepository = TargetRepository,
        TargetBranch = TargetBranch,
        PolicyObject = Policy.ToDb(),
        Enabled = Enabled ?? true,
        SourceEnabled = SourceEnabled ?? false,
        SourceDirectory = SourceDirectory,
        TargetDirectory = TargetDirectory,
        PullRequestFailureNotificationTags = PullRequestFailureNotificationTags,
        ExcludedAssets = ExcludedAssets == null ? [] : [..ExcludedAssets.Select(asset => new Data.Models.AssetFilter() { Filter = asset })],
    };
}
