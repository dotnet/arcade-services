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

public class Subscription
{
    private string _sourceRepository;
    private string _targetRepository;
    private string _branch;

    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public Guid Id { get; set; }

    public int ChannelId { get; set; }

    public Channel Channel { get; set; }

    public string SourceRepository
    {
        get
        {
            return AzureDevOpsClient.NormalizeUrl(_sourceRepository);
        }

        set
        {
            _sourceRepository = AzureDevOpsClient.NormalizeUrl(value);
        }
    }

    public string TargetRepository
    {
        get
        {
            return AzureDevOpsClient.NormalizeUrl(_targetRepository);
        }

        set
        {
            _targetRepository = AzureDevOpsClient.NormalizeUrl(value);
        }
    }

    public string TargetBranch
    {
        get
        {
            return GitHelpers.NormalizeBranchName(_branch);
        }
        set
        {
            _branch = GitHelpers.NormalizeBranchName(value);
        }
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
    /// Dependencies to ignore when synchronizing code of source-enabled subscriptions.
    /// </summary>
    public List<AssetFilter> ExcludedDependencies { get; set; }

    [NotMapped]
    public SubscriptionPolicy PolicyObject
    {
        get => PolicyString == null ? null : JsonConvert.DeserializeObject<SubscriptionPolicy>(PolicyString);
        set => PolicyString = value == null ? null : JsonConvert.SerializeObject(value);
    }

    public int? LastAppliedBuildId { get; set; }

    public Build LastAppliedBuild { get; set; }

    public string PullRequestFailureNotificationTags { get; set; }
}
