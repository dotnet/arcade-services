// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.Services.Utility;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Maestro.Data.Models;

public class DefaultChannel : ExternallySyncedEntity<(string, string, int)>
{
    public int Id { get; set; }

    [StringLength(300)]
    [Column(TypeName = "varchar(300)")]
    [Required]
    public string Repository
    {
        get => AzureDevOpsClient.NormalizeUrl(field);
        set => field = AzureDevOpsClient.NormalizeUrl(value);
    }

    [StringLength(100)]
    [Column(TypeName = "varchar(100)")]
    [Required]
    public string Branch
    {
        get => GitHelpers.NormalizeBranchName(field);
        set => field = GitHelpers.NormalizeBranchName(value);
    }

    [Required]
    public int ChannelId { get; set; }

    public bool Enabled { get; set; } = true;

    public Channel Channel { get; set; }

    public Namespace Namespace { get; set; }

    public (string, string, int) UniqueId => (Repository, Branch, ChannelId);
}
