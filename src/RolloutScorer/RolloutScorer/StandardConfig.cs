using System.Collections.Generic;

namespace RolloutScorer;

public static class StandardConfig
{
    public static readonly Config DefaultConfig = new Config
    {
        RepoConfigs = new List<RepoConfig>()
        {
            new RepoConfig
            {
                Repo = "dotnet-helix-service",
                BuildDefinitionIds = new List<string> { "620", "697" },
                AzdoInstance = "dnceng",
                GithubIssueLabel = "Rollout Helix",
                MaxAllowedTimeInMinutes = 360,
                ExcludeStages = new List<string>() { "Post_Deployment_Tests" },
            },
            new RepoConfig
            {
                Repo = "dotnet-helix-machines",
                BuildDefinitionIds = new List<string> { "596" },
                AzdoInstance = "dnceng",
                GithubIssueLabel = "Rollout OSOB",
                MaxAllowedTimeInMinutes = 360,
                ExcludeStages = new List<string> { "Validate", "Cleanup", "Validate_OnPrem" },
            },
            new RepoConfig
            {
                Repo = "arcade-services",
                BuildDefinitionIds = new List<string> { "252", "728" },
                AzdoInstance = "dnceng",
                GithubIssueLabel = "Rollout Arcade-Services",
                MaxAllowedTimeInMinutes = 360,
                ExcludeStages = new List<string> { "Post-Deployment", "Validate deployment" },
            },
        },
        AzdoInstanceConfigs = new List<AzdoInstanceConfig>()
        { 
            new AzdoInstanceConfig
            {
                Name = "dnceng",
                Project = "internal",
                PatSecretName = "dn-bot-dnceng-build-r-code-r-release-r-pat",
                KeyVaultUri = "https://engkeyvault.vault.azure.net",
            } 
        },
        RolloutWeightConfig = new RolloutWeightConfig
        {
            RolloutOverTimePoints = 50,
            PointsPerIssue = 1,
            PointsPerHotfix = 5,
            PointsPerRollback = 10,
            DowntimeMinutesPerPoint = 1,
            FailurePoints = 50,
        },
        GithubConfig = new GithubConfig
        {
            ScorecardsGithubOrg = "dotnet",
            ScorecardsGithubRepo = "arcade",
            ScorecardsDirectoryPath = "Documentation/TeamProcess/Rollout-Scorecards/",
        },
    };
}
