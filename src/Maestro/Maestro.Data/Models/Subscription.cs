// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.Services.Utility;
using Newtonsoft.Json;

namespace Maestro.Data.Models;

public class Subscription : ExternallySyncedEntity<Guid>
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public Guid Id { get; set; }

    public int ChannelId { get; set; }

    public Channel Channel { get; set; }

    public string SourceRepository
    {
        get => AzureDevOpsClient.NormalizeUrl(field);
        set => field = AzureDevOpsClient.NormalizeUrl(value);
    }

    public string TargetRepository
    {
        get => AzureDevOpsClient.NormalizeUrl(field);
        set => field = AzureDevOpsClient.NormalizeUrl(value);
    }

    public string TargetBranch
    {
        get => GitHelpers.NormalizeBranchName(field);
        set => field = GitHelpers.NormalizeBranchName(value);
    }

    [Column("Policy")]
    public string PolicyString { get; set; }

    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Denotes whether sources are also synchronized.
    /// Source or target repository must be a VMR.
    /// </summary>
    public bool SourceEnabled { get; set; }

    /// <summary>
    /// Denotes the directory of the VMR which are the sources synchronized from.
    /// (for VMR->repo subscriptions)
    /// Only Source or Target repository can be set at a time.
    /// </summary>
    public string SourceDirectory { get; set; }

    /// <summary>
    /// Denotes the directory in the VMR which are the sources synchronized into.
    /// (for repo->VMR subscriptions)
    /// Only Source or Target repository can be set at a time.
    /// </summary>
    public string TargetDirectory { get; set; }

    /// <summary>
    /// Dependencies to ignore when updating dependencies in subscriptions.
    /// </summary>
    public List<AssetFilter> ExcludedAssets { get; set; }

    [NotMapped]
    public SubscriptionPolicy PolicyObject
    {
        get => PolicyString == null ? null : JsonConvert.DeserializeObject<SubscriptionPolicy>(PolicyString);
        set => PolicyString = value == null ? null : JsonConvert.SerializeObject(value);
    }

    public int? LastAppliedBuildId { get; set; }

    public Build LastAppliedBuild { get; set; }

    public string PullRequestFailureNotificationTags { get; set; }

    public Namespace Namespace { get; set; }

    public Guid UniqueId => Id;
}
