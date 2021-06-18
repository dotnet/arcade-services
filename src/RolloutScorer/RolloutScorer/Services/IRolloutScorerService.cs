using Newtonsoft.Json.Linq;
using Octokit;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace RolloutScorer.Services
{
    public interface IRolloutScorerService
    {
        Task InitAsync(Models.RolloutScorer rolloutScorer);
        Task CollectStages(Models.ScorecardBuildBreakdown buildBreakdown, Models.RolloutScorer rolloutScorer);
        TimeSpan CalculateTimeToRollout(Models.RolloutScorer rolloutScorer);
        Task<(int numHotfixes, int numRollbacks)> CalculateNumHotfixesAndRollbacksFromAzdoAsync(Models.RolloutScorer rolloutScorer);
        bool DetermineFailure(List<Octokit.Issue> githubIssues, Models.RolloutScorer rolloutScorer);
        Task<TimeSpan> CalculateDowntimeAsync(List<Octokit.Issue> githubIssues, Models.RolloutScorer rolloutScorer);
        Task<List<Octokit.Issue>> GetRolloutIssuesFromGithubAsync(Models.RolloutScorer rolloutScorer);
        Task<List<Octokit.Issue>> GetAllIssuesFromSearchAsync(SearchIssuesRequest searchIssuesRequest);
        void SetupHttpClient(string azdoPat);
        void SetupGithubClient(string githubPat);
        Task<JObject> GetAzdoApiResponseAsync(string apiRequest);
    }
}
