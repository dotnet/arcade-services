// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace ProductConstructionService.DependencyFlow.Models;

/// <summary>
/// Represents the backflow status for a VMR commit.
/// This is stored in Redis cache.
/// </summary>
public class BackflowStatus
{
    /// <summary>
    /// VMR commit SHA this status is for.
    /// </summary>
    public required string VmrCommitSha { get; init; }

    /// <summary>
    /// When this status was computed.
    /// </summary>
    public required DateTimeOffset ComputationTimestamp { get; init; }

    /// <summary>
    /// Backflow status for each branch.
    /// Key is branch name (e.g., "main", "internal/main").
    /// </summary>
    public required Dictionary<string, BranchBackflowStatus> BranchStatuses { get; init; }
}

/// <summary>
/// Represents backflow status for a specific branch.
/// </summary>
public class BranchBackflowStatus
{
    /// <summary>
    /// The branch name.
    /// </summary>
    public required string Branch { get; init; }

    /// <summary>
    /// Default channel ID for this branch.
    /// </summary>
    public int? DefaultChannelId { get; init; }

    /// <summary>
    /// Backflow status for each subscription on this branch.
    /// </summary>
    public required List<SubscriptionBackflowStatus> SubscriptionStatuses { get; init; }
}

/// <summary>
/// Represents backflow status for a single subscription.
/// </summary>
public class SubscriptionBackflowStatus
{
    /// <summary>
    /// Target repository where code is being backflowed to.
    /// </summary>
    public required string TargetRepository { get; init; }

    /// <summary>
    /// Target branch in the repository.
    /// </summary>
    public required string TargetBranch { get; init; }

    /// <summary>
    /// Last VMR commit SHA that was backflowed to this target.
    /// </summary>
    public required string LastBackflowedSha { get; init; }

    /// <summary>
    /// Number of commits between the input SHA and the last backflowed SHA.
    /// For public branches, this excludes internal-only commits.
    /// </summary>
    public required int CommitDistance { get; init; }

    /// <summary>
    /// Subscription ID.
    /// </summary>
    public required Guid SubscriptionId { get; init; }
}
