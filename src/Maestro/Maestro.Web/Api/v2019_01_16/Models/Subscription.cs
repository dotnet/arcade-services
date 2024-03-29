// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;

namespace Maestro.Web.Api.v2019_01_16.Models;

public class Subscription
{
    public Subscription(Data.Models.Subscription other)
    {
        ArgumentNullException.ThrowIfNull(other);

        Id = other.Id;
        Channel = other.Channel == null ? null : new v2018_07_16.Models.Channel(other.Channel);
        LastAppliedBuild = other.LastAppliedBuild == null ? null : new Build(other.LastAppliedBuild);
        SourceRepository = other.SourceRepository;
        TargetRepository = other.TargetRepository;
        TargetBranch = other.TargetBranch;
        Enabled = other.Enabled;
        Policy = new v2018_07_16.Models.SubscriptionPolicy(other.PolicyObject);
    }

    public Guid Id { get; }

    public v2018_07_16.Models.Channel Channel { get; }

    public string SourceRepository { get; }

    public string TargetRepository { get; }

    public string TargetBranch { get; }

    public v2018_07_16.Models.SubscriptionPolicy Policy { get; }

    public Build LastAppliedBuild { get; }

    public bool Enabled { get; }
}
