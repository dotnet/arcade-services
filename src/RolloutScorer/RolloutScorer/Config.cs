using System.Collections.Generic;

namespace RolloutScorer
{
    public class Config
    {
        public List<RepoConfig> RepoConfigs { get; set; }
        public List<AzdoInstanceConfig> AzdoInstanceConfigs { get; set; }
        public RolloutWeightConfig RolloutWeightConfig { get; set; }
        public GithubConfig GithubConfig { get; set; }
    }

    public class AzdoInstanceConfig
    {
        public string Name { get; set; }
        public string Project { get; set; }
        public string PatSecretName { get; set; }
        public string KeyVaultUri { get; set; }
    }

    public class RepoConfig
    {
        public string Repo { get; set; }
        public List<string> BuildDefinitionIds { get; set; }
        public string AzdoInstance { get; set; }
        public string GithubIssueLabel { get; set; }
        public int ExpectedTime { get; set; }
        public List<string> ExcludeStages { get; set; }
    }

    public class RolloutWeightConfig
    {
        public int RolloutMinutesPerPoint { get; set; }
        public int PointsPerIssue { get; set; }
        public int PointsPerHotfix { get; set; }
        public int PointsPerRollback { get; set; }
        public int DowntimeMinutesPerPoint { get; set; }
        public int FailurePoints { get; set; }
    }

    public class GithubConfig
    {
        public string ScorecardsGithubOrg { get; set; }
        public string ScorecardsGithubRepo { get; set; }
        public string ScorecardsDirectoryPath { get; set; }
    }
}
