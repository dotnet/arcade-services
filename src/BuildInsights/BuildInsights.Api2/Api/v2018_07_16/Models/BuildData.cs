// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.ComponentModel.DataAnnotations;

#nullable disable
namespace ProductConstructionService.Api.v2018_07_16.Models;

public class BuildData
{
    [Required]
    public string Repository { get; set; }

    public string Branch { get; set; }

    [Required]
    public string Commit { get; set; }

    [Required]
    public string BuildNumber { get; set; }

    public List<AssetData> Assets { get; set; }

    public List<int> Dependencies { get; set; }

    public Maestro.Data.Models.Build ToDb()
    {
        var isGitHubInfo = Repository.Contains("github", StringComparison.OrdinalIgnoreCase);

        return new Maestro.Data.Models.Build
        {
            Commit = Commit,
            AzureDevOpsBuildNumber = BuildNumber,
            Assets = [.. Assets.Select(a => a.ToDb())],

            GitHubRepository = isGitHubInfo ? Repository : null,
            GitHubBranch = isGitHubInfo ? Branch : null,

            AzureDevOpsRepository = isGitHubInfo ? null : Repository,
            AzureDevOpsBranch = isGitHubInfo ? null : Branch,
        };
    }
}
