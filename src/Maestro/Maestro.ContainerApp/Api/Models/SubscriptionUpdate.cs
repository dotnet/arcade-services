// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Maestro.ContainerApp.Api.Models;

public class SubscriptionUpdate
{
    public string? ChannelName { get; set; }
    public string? SourceRepository { get; set; }
    public SubscriptionPolicy? Policy { get; set; }
    public bool? Enabled { get; set; }
    public string? PullRequestFailureNotificationTags { get; set; }
}
