using Octokit;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace RolloutScorer
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

        public async static Task<Scorecard> CreateScorecardAsync(RolloutScorer rolloutScorer)
        {
            (int numHotfixes, int numRollbacks) = await rolloutScorer.CalculateNumHotfixesAndRollbacksFromAzdoAsync();
            List<Issue> githubIssues = await rolloutScorer.GetRolloutIssuesFromGithubAsync();
            string repoLabel = rolloutScorer.RepoConfig.GithubIssueLabel;
            Scorecard scorecard = new Scorecard
            {
                Repo = rolloutScorer.RepoConfig,
                Date = rolloutScorer.RolloutStartDate,
                TimeToRollout = rolloutScorer.CalculateTimeToRollout(),
                CriticalIssues = githubIssues
                    .Count(issue => Utilities.IssueContainsRelevantLabels(issue, GithubLabelNames.IssueLabel, repoLabel, rolloutScorer.Log, rolloutScorer.LogLevel)),
                Hotfixes = numHotfixes + githubIssues
                    .Count(issue => Utilities.IssueContainsRelevantLabels(issue, GithubLabelNames.HotfixLabel, repoLabel, rolloutScorer.Log, rolloutScorer.LogLevel)),
                Rollbacks = numRollbacks + githubIssues
                    .Count(issue => Utilities.IssueContainsRelevantLabels(issue, GithubLabelNames.RollbackLabel, repoLabel, rolloutScorer.Log, rolloutScorer.LogLevel)),
                Downtime = await rolloutScorer.CalculateDowntimeAsync(githubIssues) + rolloutScorer.Downtime,
                Failure = rolloutScorer.DetermineFailure(githubIssues) || rolloutScorer.Failed,
                BuildBreakdowns = rolloutScorer.BuildBreakdowns,
                RolloutWeightConfig = rolloutScorer.RolloutWeightConfig,
                GithubIssues = githubIssues,
            };

            // Critical issues and manual hotfixes/rollbacks need to be included in the build breakdowns, but it isn't possible to determine which
            // builds they belong to; so we'll just append the issues to the first build and the hotfixes/rollbacks to the last
            if (scorecard.BuildBreakdowns.Count > 0)
            {
                scorecard.BuildBreakdowns.Sort((x,y) => x.BuildSummary.BuildNumber.CompareTo(y.BuildSummary.BuildNumber));

                // Critical issues are assumed to have been caused by the first deployment
                ScorecardBuildBreakdown firstDeployment = scorecard.BuildBreakdowns.First();
                firstDeployment.Score.CriticalIssues += scorecard.CriticalIssues;
                firstDeployment.Score.GithubIssues.AddRange(scorecard.GithubIssues
                    .Where(issue => Utilities.IssueContainsRelevantLabels(issue, GithubLabelNames.IssueLabel, repoLabel, rolloutScorer.Log, rolloutScorer.LogLevel)));

                // Hotfixes & rollbacks are assumed to have taken place in the last deployment
                // This is likely incorrect given >2 deployments but can be manually adjusted if necessary
                ScorecardBuildBreakdown lastDeployment = scorecard.BuildBreakdowns.Last();
                lastDeployment.Score.Hotfixes += rolloutScorer.ManualHotfixes;
                lastDeployment.Score.Rollbacks += rolloutScorer.ManualRollbacks;
                lastDeployment.Score.GithubIssues.AddRange(scorecard.GithubIssues
                    .Where(issue => 
                    Utilities.IssueContainsRelevantLabels(issue, GithubLabelNames.HotfixLabel, repoLabel, rolloutScorer.Log, rolloutScorer.LogLevel) ||
                    Utilities.IssueContainsRelevantLabels(issue, GithubLabelNames.RollbackLabel, repoLabel, rolloutScorer.Log, rolloutScorer.LogLevel)));
            }

            return scorecard;
        }

        /// <summary>
        /// Parses a Scorecard object from a given CSV file
        /// </summary>
        /// <param name="filePath">Path to CSV file</param>
        /// <param name="config">Config object to use during parsing</param>
        /// <returns></returns>
        public async static Task<Scorecard> ParseScorecardFromCsvAsync(string filePath, Config config)
        {
            using (StreamReader file = new StreamReader(filePath))
            {
                Scorecard scorecard = new Scorecard
                {

                    RolloutWeightConfig = config.RolloutWeightConfig
                };

                string[] rolloutInfo = (await file.ReadLineAsync()).Split(',');
                scorecard.Repo = config.RepoConfigs[rolloutInfo[0]];
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

        /// <summary>
        /// Outputs the scorecard to a CSV file
        /// </summary>
        /// <param name="filePath">Path the CSV file should be output to</param>
        /// <returns>Exit code (0 = success)</returns>
        public async Task<int> Output(string filePath)
        {
            try
            {
                using (StreamWriter writer = File.CreateText(filePath))
                {
                    await writer.WriteLineAsync($"{Repo.Repo},{Date.Date.ToShortDateString()}");
                    await writer.WriteLineAsync();
                    await writer.WriteLineAsync($"Time to Rollout,Critical Issues,Issue Links,Hotfixes,Hotfix Links,Rollbacks,Rollback Links,Downtime,Failure,Failure Links");
                    await writer.WriteLineAsync($"{TimeToRollout:c}," +
                        $"{CriticalIssues},{string.Join(";", GithubIssues.Where(issue => Utilities.IssueContainsRelevantLabels(issue, GithubLabelNames.IssueLabel, Repo.GithubIssueLabel)).Select(issue => issue.HtmlUrl))}," +
                        $"{Hotfixes},{string.Join(";", GithubIssues.Where(issue => Utilities.IssueContainsRelevantLabels(issue, GithubLabelNames.HotfixLabel, Repo.GithubIssueLabel)).Select(issue => issue.HtmlUrl))}," +
                        $"{Rollbacks},{string.Join(";", GithubIssues.Where(issue => Utilities.IssueContainsRelevantLabels(issue, GithubLabelNames.RollbackLabel, Repo.GithubIssueLabel)).Select(issue => issue.HtmlUrl))}," +
                        $"{Downtime:c}," +
                        $"{Failure},{string.Join(";", GithubIssues.Where(issue => Utilities.IssueContainsRelevantLabels(issue, GithubLabelNames.FailureLabel, Repo.GithubIssueLabel)).Select(issue => issue.HtmlUrl))}");
                    await writer.WriteLineAsync();
                    await writer.WriteLineAsync($"Build,Link,Time to Rollout,Critical Issues,Hotfixes,Rollbacks,Downtime");
                    foreach (ScorecardBuildBreakdown buildBreakdown in BuildBreakdowns)
                    {
                        await writer.WriteLineAsync($"{buildBreakdown.BuildSummary.BuildNumber},{buildBreakdown.BuildSummary.WebLink},{buildBreakdown.Score.TimeToRollout:c}," +
                            $"{buildBreakdown.Score.CriticalIssues},{buildBreakdown.Score.Hotfixes},{buildBreakdown.Score.Rollbacks},{buildBreakdown.Score.Downtime:c}");
                    }
                }
            }
            catch (IOException e)
            {
                Utilities.WriteError($"File {filePath} could not be opened for writing; do you have it open in Excel?");
                Utilities.WriteError(e.Message);
                return 1;
            }

            return 0;
        }
    }

    public class ScorecardBuildBreakdown
    {
        public BuildSummary BuildSummary { get; }
        public Scorecard Score { get; }
        public ScorecardBuildBreakdown(BuildSummary buildSummary)
        {
            BuildSummary = buildSummary;
            Score = new Scorecard();
        }
    }
}
