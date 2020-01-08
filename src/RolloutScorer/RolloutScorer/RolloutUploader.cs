using Octokit;
using Octokit.Helpers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.WindowsAzure.Storage.Table;
using Microsoft.Azure.KeyVault.Models;
using Microsoft.DotNet.Services.Utility;
using Microsoft.Extensions.Logging;

namespace RolloutScorer
{
    public class RolloutUploader
    {
        private const string _gitFileBlobMode = "100644";

        private struct ScorecardBatch
        {
            public DateTimeOffset Date;
            public List<Scorecard> Scorecards;
        }

        private struct RepoMarkdown
        {
            public string Summary;
            public string Breakdown;
        }

        /// <summary>
        /// Uploads results to GitHub/Azure Table Storage
        /// </summary>
        /// <param name="scorecardFiles">List of paths to scorecard CSV files</param>
        /// <param name="config">Parsed Config object representing config file</param>
        /// <param name="githubClient">An authenticated Octokit.GitHubClient instance</param>
        /// <param name="storageAccountKey">A secret bundle containing the key to the rollout scorecards storage account</param>
        /// <returns>Exit code (0 = success, 1 = failure)</returns>
        public async static Task<int> UploadResultsAsync(List<string> scorecardFiles, Config config, GitHubClient githubClient, string storageAccountKey, ILogger log = null)
        {
            try
            {
                await UploadResultsAsync(new List<Scorecard>(
                    await Task.WhenAll(scorecardFiles.Select(
                    file => Scorecard.ParseScorecardFromCsvAsync(file, config)
                    ))), githubClient, storageAccountKey, config.GithubConfig);
            }
            catch (IOException e)
            {
                Utilities.WriteError($"File could not be opened for writing; do you have it open in Excel?", log);
                Utilities.WriteError(e.Message, log);
                return 1;
            }

            string successMessage = $"Successfully uploaded:\n\t{string.Join("\n\t", scorecardFiles)}";
            if (log == null)
            {
                Console.WriteLine(successMessage);
            }
            else
            {
                log.LogInformation(successMessage);
            }

            return 0;
        }

        /// <summary>
        /// Uploads results to GitHub/Azure Table Storage
        /// </summary>
        /// <param name="scorecards">List of Scorecard instances to be uploaded</param>
        /// <param name="githubClient">An authenticated Octokit.GitHubClient instance</param>
        /// <param name="storageAccountKey">Key to the rollout scorecards storage account</param>
        /// <param name="githubConfig">GitHubConfig object representing config</param>
        public async static Task UploadResultsAsync(List<Scorecard> scorecards, GitHubClient githubClient, string storageAccountKey, GithubConfig githubConfig)
        {
            // We batch the scorecards by date so they can be sorted into markdown files properly
            IEnumerable<ScorecardBatch> scorecardBatches = scorecards
                .GroupBy(s => s.Date).Select(g => new ScorecardBatch { Date = g.Key, Scorecards = g.ToList() });

            Reference masterBranch = await githubClient.Git.Reference
                .Get(githubConfig.ScorecardsGithubOrg, githubConfig.ScorecardsGithubRepo, "heads/master");
            string newBranchName = $"{DateTime.Today:yyyy-MM-dd}-Scorecard-Update";
            string newBranchRef = $"heads/{newBranchName}";
            Reference newBranch;

            // If this succeeds than the branch exists and we should update it directly
            try
            {
                newBranch = await githubClient.Git.Reference.Get(githubConfig.ScorecardsGithubOrg,
                    githubConfig.ScorecardsGithubRepo, newBranchRef);
            }
            // If not, we've got to create the new branch
            catch (NotFoundException)
            {
                newBranch = await githubClient.Git.Reference.CreateBranch(githubConfig.ScorecardsGithubOrg,
                    githubConfig.ScorecardsGithubRepo, newBranchName, masterBranch);
            }

            TreeResponse currentTree = await githubClient.Git.Tree.Get(githubConfig.ScorecardsGithubOrg,
                githubConfig.ScorecardsGithubRepo, newBranchRef);
            NewTree newTree = new NewTree
            {
                BaseTree = currentTree.Sha,
            };

            // We loop over the batches and generate a markdown file for each rollout date
            foreach (ScorecardBatch scorecardBatch in scorecardBatches)
            {
                List<RepoMarkdown> repoMarkdowns = scorecardBatch.Scorecards.Select(s => CreateRepoMarkdown(s)).ToList();

                string scorecardBatchMarkdown = $"# {scorecardBatch.Date.Date:dd MMMM yyyy} Rollout Summaries\n\n" +
                    $"{string.Join('\n', repoMarkdowns.Select(md => md.Summary))}\n" +
                    $"# Itemized Scorecard\n\n" +
                    $"{string.Join('\n', repoMarkdowns.Select(md => md.Breakdown))}";

                string scorecardBatchFilePath = 
                    $"{githubConfig.ScorecardsDirectoryPath}Scorecard_{scorecardBatch.Date.Date:yyyy-MM-dd}.md";

                NewTreeItem markdownBlob = new NewTreeItem
                {
                    Path = scorecardBatchFilePath,
                    Mode = _gitFileBlobMode,
                    Type = TreeType.Blob,
                    Content = scorecardBatchMarkdown,
                };
                newTree.Tree.Add(markdownBlob);
            }

            TreeResponse treeResponse = await githubClient.Git.Tree.Create(githubConfig.ScorecardsGithubOrg, githubConfig.ScorecardsGithubRepo, newTree);

            // Commit the new response to the new branch
            NewCommit newCommit = new NewCommit("Add scorecards for " +
                string.Join(", ", scorecardBatches.Select(s => s.Date.Date.ToString("yyyy-MM-dd"))),
                treeResponse.Sha,
                newBranch.Object.Sha);
            Commit commit = await githubClient.Git.Commit
                .Create(githubConfig.ScorecardsGithubOrg, githubConfig.ScorecardsGithubRepo, newCommit);

            ReferenceUpdate update = new ReferenceUpdate(commit.Sha);
            Reference updatedRef = await githubClient.Git.Reference.Update(githubConfig.ScorecardsGithubOrg,
                githubConfig.ScorecardsGithubRepo, newBranchRef, update);

            PullRequestRequest prRequest = new PullRequestRequest
            {
                Base = "master",
                Head = newBranchName,
                State = ItemStateFilter.Open,
            };
            // If an open PR exists already, we shouldn't try to create a new one
            List<PullRequest> prs =
                (await githubClient.PullRequest.GetAllForRepository(githubConfig.ScorecardsGithubOrg, githubConfig.ScorecardsGithubRepo)).ToList();
            if (!prs.Any(pr => pr.Head.Ref == newBranchName))
            {
                NewPullRequest newPullRequest = new NewPullRequest(newCommit.Message, newBranchName, "master");
                await githubClient.PullRequest.Create(githubConfig.ScorecardsGithubOrg, githubConfig.ScorecardsGithubRepo, newPullRequest);
            }

            // Upload the results to Azure Table Storage (will overwrite previous entries with new data if necessary)
            CloudTable table = Utilities.GetScorecardsCloudTable(storageAccountKey);
            foreach (Scorecard scorecard in scorecards)
            {
                ScorecardEntity scorecardEntity = new ScorecardEntity(scorecard.Date, scorecard.Repo.Repo)
                {
                    TotalScore = scorecard.TotalScore,
                    TimeToRolloutSeconds = scorecard.TimeToRollout.TotalSeconds,
                    CriticalIssues = scorecard.CriticalIssues,
                    Hotfixes = scorecard.Hotfixes,
                    Rollbacks = scorecard.Rollbacks,
                    DowntimeSeconds = scorecard.Downtime.TotalSeconds,
                    Failure = scorecard.Failure,
                    TimeToRolloutScore = scorecard.TimeToRolloutScore,
                    CriticalIssuesScore = scorecard.CriticalIssueScore,
                    HotfixScore = scorecard.HotfixScore,
                    RollbackScore = scorecard.RollbackScore,
                    DowntimeScore = scorecard.DowntimeScore,
                };
                await table.ExecuteAsync(TableOperation.InsertOrReplace(scorecardEntity));
            }
        }

        private static RepoMarkdown CreateRepoMarkdown(Scorecard scorecard)
        {
            string summary = $"## {scorecard.Repo.Repo}\n\n" +
                $"|              Metric              |   Value  |  Target  |   Score   |\n" +
                $"|:--------------------------------:|:--------:|:--------:|:---------:|\n" +
                $"| Time to Rollout                  | {scorecard.TimeToRollout} | {TimeSpan.FromMinutes(scorecard.Repo.ExpectedTime)} |     {scorecard.TimeToRolloutScore}     |\n" +
                $"| Critical/blocking issues created |     {scorecard.CriticalIssues}    |    0     |     {scorecard.CriticalIssueScore}     |\n" +
                $"| Hotfixes                         |     {scorecard.Hotfixes}    |    0     |     {scorecard.HotfixScore}     |\n" +
                $"| Rollbacks                        |     {scorecard.Rollbacks}    |    0     |     {scorecard.RollbackScore}     |\n" +
                $"| Service downtime                 | {scorecard.Downtime} | 00:00:00 |     {scorecard.DowntimeScore}     |\n" +
                $"| Failed to rollout                |   {scorecard.Failure.ToString().ToUpperInvariant()}  |   FALSE  |     {(scorecard.Failure ? scorecard.RolloutWeightConfig.FailurePoints : 0)}     |\n" +
                $"| Total                            |          |          |   **{scorecard.TotalScore}**   |\n\n" +
                $"{CreateGithubIssueUrisMarkdown(scorecard.GithubIssueUris)}";

            string breakdown = "";

            if (scorecard.BuildBreakdowns.Count > 0)
            {
                string breakdownTableHeader = "| Metric |";
                string breakdownTableColumns = "|:-----:|";
                string breakdownTimeToRolloutRow = "| Time to Rollout |";
                string breakdownCriticalIssuesRow = "| Critical/blocking issues created |";
                string breakdownHotfixesRow = "| Hotfixes |";
                string breakdownRollbacksRow = "| Rollbacks |";
                string breakdownDowntime = "| Service downtime |";

                foreach (ScorecardBuildBreakdown scorecardBreakdown in scorecard.BuildBreakdowns)
                {
                    breakdownTableHeader += $" [{scorecardBreakdown.BuildSummary.BuildNumber}]({scorecardBreakdown.BuildSummary.WebLink}) |";
                    breakdownTableColumns += ":-----:|";
                    breakdownTimeToRolloutRow += $" {scorecardBreakdown.Score.TimeToRollout} |";
                    breakdownCriticalIssuesRow += $" {scorecardBreakdown.Score.CriticalIssues} |";
                    breakdownHotfixesRow += $" {scorecardBreakdown.Score.Hotfixes} |";
                    breakdownRollbacksRow += $" {scorecardBreakdown.Score.Rollbacks} |";
                    breakdownDowntime += $" {scorecardBreakdown.Score.Downtime} |";
                }

                breakdown = $"## {scorecard.Repo.Repo}\n\n" +
                    $"{breakdownTableHeader}\n" +
                    $"{breakdownTableColumns}\n" +
                    $"{breakdownTimeToRolloutRow}\n" +
                    $"{breakdownCriticalIssuesRow}\n" +
                    $"{breakdownHotfixesRow}\n" +
                    $"{breakdownRollbacksRow}\n" +
                    $"{breakdownDowntime}\n\n";
            }

            return new RepoMarkdown { Summary = summary, Breakdown = breakdown };
        }

        private static string CreateGithubIssueUrisMarkdown(List<string> githubIssueUris)
        {
            if (githubIssueUris.Count == 0)
            {
                return "";
            }
            githubIssueUris.Sort();
            Dictionary<string, string> githubIssues = githubIssueUris.Distinct().ToDictionary(i => i.Substring(i.LastIndexOf('/') + 1), i => i);

            return $"Relevant GitHub issues: {string.Join(", ", githubIssues.Select(i => $"[#{i.Key}]({i.Value})"))}";
        }
    }
}
