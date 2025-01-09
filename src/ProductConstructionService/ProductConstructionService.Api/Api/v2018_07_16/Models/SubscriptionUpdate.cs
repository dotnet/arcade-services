// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.


// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#nullable disable
namespace ProductConstructionService.Api.v2018_07_16.Models;

public class SubscriptionUpdate
{
    public string ChannelName { get; set; }
    public string SourceRepository { get; set; }
    public SubscriptionPolicy Policy { get; set; }
    public bool? Enabled { get; set; }
}
