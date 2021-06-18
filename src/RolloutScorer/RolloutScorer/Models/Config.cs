using System.Collections.Generic;

namespace RolloutScorer.Models
{
    public class Config
    {
        public List<RepoConfig> RepoConfigs { get; set; }
        public List<AzdoInstanceConfig> AzdoInstanceConfigs { get; set; }
        public RolloutWeightConfig RolloutWeightConfig { get; set; }
        public GithubConfig GithubConfig { get; set; }
    }
}
