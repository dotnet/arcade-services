using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using RolloutScorer.Models;
using RolloutScorer.Services;

namespace RolloutScorer.Providers
{
    public class PersistenceProvider : IPersistenceService
    {
        private readonly ILogger<PersistenceProvider> _logger;

        public PersistenceProvider(ILogger<PersistenceProvider> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Outputs the scorecard to a CSV file
        /// </summary>
        /// <param name="filePath">Path the CSV file should be output to</param>
        /// <returns>Exit code (0 = success)</returns>
        public async Task<int> WriteScorecardToCSV(Scorecard scorecard, string filePath)
        {
            try
            {
                using (StreamWriter writer = File.CreateText(filePath))
                {
                    await writer.WriteLineAsync($"{scorecard.Repo.Repo},{scorecard.Date.Date.ToShortDateString()}");
                    await writer.WriteLineAsync();
                    await writer.WriteLineAsync($"Time to Rollout,Critical Issues,Issue Links,Hotfixes,Hotfix Links,Rollbacks,Rollback Links,Downtime,Failure,Failure Links");
                    await writer.WriteLineAsync($"{scorecard.TimeToRollout:c}," +
                        $"{scorecard.CriticalIssues},{string.Join(";", scorecard.GithubIssues.Where(issue => Utilities.IssueContainsRelevantLabels(issue, GithubLabelNames.IssueLabel, scorecard.Repo.GithubIssueLabel)).Select(issue => issue.HtmlUrl))}," +
                        $"{scorecard.Hotfixes},{string.Join(";", scorecard.GithubIssues.Where(issue => Utilities.IssueContainsRelevantLabels(issue, GithubLabelNames.HotfixLabel, scorecard.Repo.GithubIssueLabel)).Select(issue => issue.HtmlUrl))}," +
                        $"{scorecard.Rollbacks},{string.Join(";", scorecard.GithubIssues.Where(issue => Utilities.IssueContainsRelevantLabels(issue, GithubLabelNames.RollbackLabel, scorecard.Repo.GithubIssueLabel)).Select(issue => issue.HtmlUrl))}," +
                        $"{scorecard.Downtime:c}," +
                        $"{scorecard.Failure},{string.Join(";", scorecard.GithubIssues.Where(issue => Utilities.IssueContainsRelevantLabels(issue, GithubLabelNames.FailureLabel, scorecard.Repo.GithubIssueLabel)).Select(issue => issue.HtmlUrl))}");
                    await writer.WriteLineAsync();
                    await writer.WriteLineAsync($"Build,Link,Time to Rollout,Critical Issues,Hotfixes,Rollbacks,Downtime");
                    foreach (ScorecardBuildBreakdown buildBreakdown in scorecard.BuildBreakdowns)
                    {
                        await writer.WriteLineAsync($"{buildBreakdown.BuildSummary.BuildNumber},{buildBreakdown.BuildSummary.WebLink},{buildBreakdown.Score.TimeToRollout:c}," +
                            $"{buildBreakdown.Score.CriticalIssues},{buildBreakdown.Score.Hotfixes},{buildBreakdown.Score.Rollbacks},{buildBreakdown.Score.Downtime:c}");
                    }
                }
            }
            catch (IOException e)
            {
                _logger.LogError($"File {filePath} could not be opened for writing; do you have it open in Excel?");
                _logger.LogError(e.Message);
                return 1;
            }

            return 0;
        }
    }
}
