using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Octokit;
using RolloutScorer.Models;
using RolloutScorer.Services;

namespace RolloutScorer.Providers
{
    public class ScorecardProvider : IScorecardService
    {
        private readonly ILogger<ScorecardProvider> _logger;
        private readonly IRolloutScorerService _rolloutScorerService;
        private readonly IIssueService _issueService;

        public ScorecardProvider(ILogger<ScorecardProvider> logger,
            IRolloutScorerService rolloutScorerService,
            IIssueService issueService)
        {
            _logger = logger;
            _rolloutScorerService = rolloutScorerService;
            _issueService = issueService;
        }

        public async Task<Scorecard> CreateScorecardAsync(Models.RolloutScorer rolloutScorer)
        {
            (int numHotfixes, int numRollbacks) = await _rolloutScorerService.CalculateNumHotfixesAndRollbacksFromAzdoAsync(rolloutScorer);
            List<Issue> githubIssues = await _rolloutScorerService.GetRolloutIssuesFromGithubAsync(rolloutScorer);
            string repoLabel = rolloutScorer.RepoConfig.GithubIssueLabel;
            Scorecard scorecard = new Scorecard
            {
                Repo = rolloutScorer.RepoConfig,
                Date = rolloutScorer.RolloutStartDate,
                TimeToRollout = _rolloutScorerService.CalculateTimeToRollout(rolloutScorer),
                CriticalIssues = githubIssues
                    .Count(issue => _issueService.IssueContainsRelevantLabels(issue, GithubLabelNames.IssueLabel, repoLabel)),
                Hotfixes = numHotfixes + githubIssues
                    .Count(issue => _issueService.IssueContainsRelevantLabels(issue, GithubLabelNames.HotfixLabel, repoLabel)),
                Rollbacks = numRollbacks + githubIssues
                    .Count(issue => _issueService.IssueContainsRelevantLabels(issue, GithubLabelNames.RollbackLabel, repoLabel)),
                Downtime = await _rolloutScorerService.CalculateDowntimeAsync(githubIssues, rolloutScorer) + rolloutScorer.Downtime,
                Failure = _rolloutScorerService.DetermineFailure(githubIssues, rolloutScorer) || rolloutScorer.Failed,
                BuildBreakdowns = rolloutScorer.BuildBreakdowns,
                RolloutWeightConfig = rolloutScorer.RolloutWeightConfig,
                GithubIssues = githubIssues,
            };

            // Critical issues and manual hotfixes/rollbacks need to be included in the build breakdowns, but it isn't possible to determine which
            // builds they belong to; so we'll just append the issues to the first build and the hotfixes/rollbacks to the last
            if (scorecard.BuildBreakdowns.Count > 0)
            {
                scorecard.BuildBreakdowns.Sort((x, y) => x.BuildSummary.BuildNumber.CompareTo(y.BuildSummary.BuildNumber));

                // Critical issues are assumed to have been caused by the first deployment
                ScorecardBuildBreakdown firstDeployment = scorecard.BuildBreakdowns.First();
                firstDeployment.Score.CriticalIssues += scorecard.CriticalIssues;
                firstDeployment.Score.GithubIssues.AddRange(scorecard.GithubIssues
                    .Where(issue => _issueService.IssueContainsRelevantLabels(issue, GithubLabelNames.IssueLabel, repoLabel)));

                // Hotfixes & rollbacks are assumed to have taken place in the last deployment
                // This is likely incorrect given >2 deployments but can be manually adjusted if necessary
                ScorecardBuildBreakdown lastDeployment = scorecard.BuildBreakdowns.Last();
                lastDeployment.Score.Hotfixes += rolloutScorer.ManualHotfixes;
                lastDeployment.Score.Rollbacks += rolloutScorer.ManualRollbacks;
                lastDeployment.Score.GithubIssues.AddRange(scorecard.GithubIssues
                    .Where(issue =>
                    _issueService.IssueContainsRelevantLabels(issue, GithubLabelNames.HotfixLabel, repoLabel) ||
                    _issueService.IssueContainsRelevantLabels(issue, GithubLabelNames.RollbackLabel, repoLabel)));
            }

            return scorecard;
        }

        /// <summary>
        /// Parses a Scorecard object from a given CSV file
        /// </summary>
        /// <param name="filePath">Path to CSV file</param>
        /// <param name="config">Config object to use during parsing</param>
        /// <returns></returns>
        public async Task<Scorecard> ParseScorecardFromCsvAsync(string filePath, Config config)
        {
            using (StreamReader file = new StreamReader(filePath))
            {
                Scorecard scorecard = new Scorecard
                {

                    RolloutWeightConfig = config.RolloutWeightConfig
                };

                string[] rolloutInfo = (await file.ReadLineAsync()).Split(',');
                scorecard.Repo = config.RepoConfigs.Find(r => r.Repo == rolloutInfo[0]);
                scorecard.Date = DateTimeOffset.Parse(rolloutInfo[1]);
                await file.ReadLineAsync();

                await file.ReadLineAsync();
                string[] rolloutScorecardSummary = (await file.ReadLineAsync()).Split(',');
                scorecard.TimeToRollout = TimeSpan.Parse(rolloutScorecardSummary[0]);
                scorecard.CriticalIssues = int.Parse(rolloutScorecardSummary[1]);
                scorecard._githubIssuesUris.AddRange(GetIssueLinksFromString(rolloutScorecardSummary[2]));
                scorecard.Hotfixes = int.Parse(rolloutScorecardSummary[3]);
                scorecard._githubIssuesUris.AddRange(GetIssueLinksFromString(rolloutScorecardSummary[4]));
                scorecard.Rollbacks = int.Parse(rolloutScorecardSummary[5]);
                scorecard._githubIssuesUris.AddRange(GetIssueLinksFromString(rolloutScorecardSummary[6]));
                scorecard.Downtime = TimeSpan.Parse(rolloutScorecardSummary[7]);
                scorecard.Failure = bool.Parse(rolloutScorecardSummary[8]);
                scorecard._githubIssuesUris.AddRange(GetIssueLinksFromString(rolloutScorecardSummary[9]));
                await file.ReadLineAsync();

                await file.ReadLineAsync();
                string[] buildBreakdownLines = (await file.ReadToEndAsync()).Split('\n');
                foreach (string breakdownLine in buildBreakdownLines)
                {
                    if (breakdownLine.Length == 0)
                    {
                        break;
                    }

                    BuildSummary buildSummary = new BuildSummary();

                    string[] breakdownSummary = breakdownLine.Split(',');
                    buildSummary.BuildNumber = breakdownSummary[0];
                    buildSummary.Links.WebLink.Href = breakdownSummary[1];

                    ScorecardBuildBreakdown buildBreakdown = new ScorecardBuildBreakdown(buildSummary);

                    buildBreakdown.Score.TimeToRollout = TimeSpan.Parse(breakdownSummary[2]);
                    buildBreakdown.Score.CriticalIssues = int.Parse(breakdownSummary[3]);
                    buildBreakdown.Score.Hotfixes = int.Parse(breakdownSummary[4]);
                    buildBreakdown.Score.Rollbacks = int.Parse(breakdownSummary[5]);
                    buildBreakdown.Score.Downtime = TimeSpan.Parse(breakdownSummary[6]);

                    scorecard.BuildBreakdowns.Add(buildBreakdown);
                }

                return scorecard;
            }
        }

        private static string[] GetIssueLinksFromString(string issueLinksString)
        {
            string[] issueLinks = issueLinksString.Split(';');
            if (issueLinks.Length > 0 && issueLinks[0].Length > 0)
            {
                return issueLinks;
            }
            else
            {
                return new string[0];
            }
        }
    }
}
