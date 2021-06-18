using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;

namespace RolloutScorer.Models
{
    public class RolloutScorer
    {
        public string Repo { get; set; }
        public string Branch { get; set; } = "refs/heads/production";
        public DateTimeOffset RolloutStartDate { get; set; }
        public DateTimeOffset RolloutEndDate { get; set; } = DateTimeOffset.Now;
        public int ManualRollbacks { get; set; } = 0;
        public int ManualHotfixes { get; set; } = 0;
        public bool AssumeNoTags { get; set; } = false;
        public TimeSpan Downtime { get; set; } = TimeSpan.Zero;
        public bool Failed { get; set; } = false;
        public string OutputFile { get; set; }
        public bool SkipOutput { get; set; } = false;
        public bool Upload { get; set; } = false;
        public bool Help { get; set; } = false;

        public RepoConfig RepoConfig { get; set; }
        public AzdoInstanceConfig AzdoConfig { get; set; }
        public RolloutWeightConfig RolloutWeightConfig { get; set; }
        public GithubConfig GithubConfig { get; set; }

        public List<ScorecardBuildBreakdown> BuildBreakdowns { get; set; } = new List<ScorecardBuildBreakdown>();        

        public ILogger Log { get; set; }
        public LogLevel LogLevel { get; set; } = LogLevel.Information;        
    }
}
