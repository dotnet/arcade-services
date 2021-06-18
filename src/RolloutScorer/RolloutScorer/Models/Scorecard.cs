using Octokit;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace RolloutScorer.Models
{
    public class Scorecard
    {
        public RepoConfig Repo { get; set; }
        public DateTimeOffset Date { get; set; }
        public TimeSpan TimeToRollout { get; set; }
        public int CriticalIssues { get; set; }
        public int Hotfixes { get; set; }
        public int Rollbacks { get; set; }
        public TimeSpan Downtime { get; set; }
        public bool Failure { get; set; }
        public List<ScorecardBuildBreakdown> BuildBreakdowns { get; set; } = new List<ScorecardBuildBreakdown>();
        public RolloutWeightConfig RolloutWeightConfig { get; set; }
        public List<Issue> GithubIssues { get; set; } = new List<Issue>();
        private List<string> _githubIssuesUris = new List<string>();
        public List<string> GithubIssueUris => _githubIssuesUris.Count == 0 ? GithubIssues.Select(issue => issue.HtmlUrl.ToString()).ToList() : _githubIssuesUris;

        public int TimeToRolloutScore => Math.Max((int)Math.Ceiling((TimeToRollout.TotalMinutes - Repo.ExpectedTime) / RolloutWeightConfig.RolloutMinutesPerPoint), 0);
        public int CriticalIssueScore => CriticalIssues * RolloutWeightConfig.PointsPerIssue;
        public int HotfixScore => Hotfixes * RolloutWeightConfig.PointsPerHotfix;
        public int RollbackScore => Rollbacks * RolloutWeightConfig.PointsPerRollback;
        public int DowntimeScore => (int)Math.Ceiling(Downtime.TotalMinutes / RolloutWeightConfig.DowntimeMinutesPerPoint);

        public int TotalScore => TimeToRolloutScore + CriticalIssueScore + HotfixScore + RollbackScore + DowntimeScore + (Failure ? RolloutWeightConfig.FailurePoints : 0);        
    }    
}
