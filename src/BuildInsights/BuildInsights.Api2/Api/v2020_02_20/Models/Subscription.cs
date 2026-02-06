// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#nullable disable

namespace ProductConstructionService.Api.v2020_02_20.Models;

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
        SourceEnabled = other.SourceEnabled;
        SourceDirectory = other.SourceDirectory;
        TargetDirectory = other.TargetDirectory;
        Policy = new v2018_07_16.Models.SubscriptionPolicy(other.PolicyObject);
        PullRequestFailureNotificationTags = other.PullRequestFailureNotificationTags;
        ExcludedAssets = other.ExcludedAssets != null ? [.. other.ExcludedAssets.Select(s => s.Filter)] : [];
    }

    public Guid Id { get; }

    public Channel Channel { get; }

    public string SourceRepository { get; }

    public string TargetRepository { get; }

    public string TargetBranch { get; }

    public v2018_07_16.Models.SubscriptionPolicy Policy { get; }

    public Build LastAppliedBuild { get; }

    public bool Enabled { get; }

    public bool SourceEnabled { get; }

    public string SourceDirectory { get; }

    public string TargetDirectory { get; }

    public string PullRequestFailureNotificationTags { get; }

    public IReadOnlyCollection<string> ExcludedAssets { get; }
}
