// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.ComponentModel.DataAnnotations;
using ProductConstructionService.Api.v2018_07_16.Models;
using Newtonsoft.Json;

#nullable disable
namespace ProductConstructionService.Api.v2019_01_16.Models;

public class BuildData
{
    [Required]
    public string Commit { get; set; }

    public List<AssetData> Assets { get; set; }

    public List<BuildRef> Dependencies { get; set; }

    public int? AzureDevOpsBuildId { get; set; }

    public int? AzureDevOpsBuildDefinitionId { get; set; }

    [Required]
    public string AzureDevOpsAccount { get; set; }

    [Required]
    public string AzureDevOpsProject { get; set; }

    [Required]
    public string AzureDevOpsBuildNumber { get; set; }

    [Required]
    public string AzureDevOpsRepository { get; set; }

    [Required]
    public string AzureDevOpsBranch { get; set; }

    public string GitHubRepository { get; set; }

    public string GitHubBranch { get; set; }

    public static bool PublishUsingPipelines
    {
        get
        {
            return true;
        }

        set { }
    }

    public bool Released { get; set; }

    public Maestro.Data.Models.Build ToDb()
    {
        return new Maestro.Data.Models.Build
        {
            GitHubRepository = GitHubRepository,
            GitHubBranch = GitHubBranch,
            AzureDevOpsBuildId = AzureDevOpsBuildId,
            AzureDevOpsBuildDefinitionId = AzureDevOpsBuildDefinitionId,
            AzureDevOpsAccount = AzureDevOpsAccount,
            AzureDevOpsProject = AzureDevOpsProject,
            AzureDevOpsBuildNumber = AzureDevOpsBuildNumber,
            AzureDevOpsRepository = AzureDevOpsRepository,
            AzureDevOpsBranch = AzureDevOpsBranch,
            Commit = Commit,
            Assets = Assets?.Select(a => a.ToDb()).ToList(),
            Released = Released
        };
    }
}

public class BuildRef
{
    [JsonConstructor]
    public BuildRef(int buildId, bool isProduct)
    {
        BuildId = buildId;
        IsProduct = isProduct;
    }

    public BuildRef(int buildId, bool isProduct, double timeToInclusionInMinutes)
    {
        BuildId = buildId;
        IsProduct = isProduct;
        TimeToInclusionInMinutes = timeToInclusionInMinutes;
    }

    public int BuildId { get; }
    public bool IsProduct { get; }
    public double TimeToInclusionInMinutes { get; set; }
}
