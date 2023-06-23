// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

namespace Maestro.Web.Api.v2020_02_20.Models;

public class SubscriptionUpdate
{
    public string ChannelName { get; set; }
    public string SourceRepository { get; set; }
    public v2018_07_16.Models.SubscriptionPolicy Policy { get; set; }
    public bool? Enabled { get; set; }
    public string PullRequestFailureNotificationTags { get; set; }
}
