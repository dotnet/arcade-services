// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using Maestro.Api.Model.v2018_07_16;

namespace Maestro.Api.Model.v2019_01_16;

public class Subscription
{
    public Subscription(Data.Models.Subscription other)
    {
        ArgumentNullException.ThrowIfNull(other);

        Id = other.Id;
        Channel = other.Channel == null ? null : new Channel(other.Channel);
        LastAppliedBuild = other.LastAppliedBuild == null ? null : new Build(other.LastAppliedBuild);
        SourceRepository = other.SourceRepository;
        TargetRepository = other.TargetRepository;
        TargetBranch = other.TargetBranch;
        Enabled = other.Enabled;
        Policy = new SubscriptionPolicy(other.PolicyObject);
    }

    public Guid Id { get; }

    public Channel Channel { get; }

    public string SourceRepository { get; }

    public string TargetRepository { get; }

    public string TargetBranch { get; }

    public SubscriptionPolicy Policy { get; }

    public Build LastAppliedBuild { get; }

    public bool Enabled { get; }
}
