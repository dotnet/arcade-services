// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using ProductConstructionService.Api.v2018_07_16.Models;

#nullable disable
namespace ProductConstructionService.Api.v2019_01_16.Models;

public class Subscription
{
    public Subscription(Maestro.Data.Models.Subscription other)
    {
        ArgumentNullException.ThrowIfNull(other);

        Id = other.Id;
        Channel = other.Channel == null ? null : new Channel(other.Channel);
        LastAppliedBuild = other.LastAppliedBuild == null ? null : new Build(other.LastAppliedBuild);
        SourceRepository = other.SourceRepository;
        TargetRepository = other.TargetRepository;
        TargetBranch = other.TargetBranch;
        Enabled = other.Enabled;
        MergePrs = other.MergePrs;
        IgnoredChecks = [.. other.IgnoredChecks];
        Policy = new SubscriptionPolicy(other.PolicyObject);
    }

    public Guid Id { get; }

    public Channel Channel { get; }

    public string SourceRepository { get; }

    public string TargetRepository { get; }

    public string TargetBranch { get; }

    public bool MergePrs { get; }

    public IReadOnlyCollection<string> IgnoredChecks { get; }

    // TODO: Remove the legacy policy model after the configuration migration.
    // https://github.com/dotnet/arcade-services/issues/6426
    public SubscriptionPolicy Policy { get; }

    public Build LastAppliedBuild { get; }

    public bool Enabled { get; }
}
