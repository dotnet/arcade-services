// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#nullable disable
namespace ProductConstructionService.Api.Api.v2020_02_20.Models;

public class SubscriptionUpdate
{
    public string ChannelName { get; set; }
    public string SourceRepository { get; set; }
    public v2018_07_16.Models.SubscriptionPolicy Policy { get; set; }
    public bool? Enabled { get; set; }
    public bool? SourceEnabled { get; set; }
    public string PullRequestFailureNotificationTags { get; set; }
    public string SourceDirectory { get; set; }
    public string TargetDirectory { get; set; }
    public IReadOnlyCollection<string> ExcludedAssets { get; set; }
}
