using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using Octokit;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Models = RolloutScorer.Models;
using RolloutScorer.Services;
using Microsoft.DotNet.GitHub.Authentication;
using Microsoft.DotNet.Internal.AzureDevOps;
using Microsoft.TeamFoundation.Build.WebApi;

namespace RolloutScorer.Providers
{
    public class RolloutScorerProvider : IRolloutScorerService
    {
        private readonly ILogger<RolloutScorerProvider> _logger;
        private readonly IGitHubClientFactory _gitHubClientFactory;
        private readonly IIssueService _issueService;
        private readonly IAzureDevOpsClientFactory _azureDevOpsClientFactory;
        private readonly BuildHttpClient _buildHttpClient;

        // The AzDO API has some 302s in it that break auth so we have to handle those ourselves
        private readonly HttpClient _httpClient = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false });
        private IGitHubClient _githubClient;
        

        public RolloutScorerProvider(
            ILogger<RolloutScorerProvider> logger,
            IGitHubClientFactory gitHubClientFactory,
            IIssueService issueService,
            IAzureDevOpsClientFactory azureDevOpsClientFactory,
            BuildHttpClient buildHttpClient)
        {
            _logger = logger;
            _gitHubClientFactory = gitHubClientFactory;
            _issueService = issueService;
            _azureDevOpsClientFactory = azureDevOpsClientFactory;
            _buildHttpClient = buildHttpClient;
        }

        public async Task InitAsync(Models.RolloutScorer rolloutScorer)
        {
            foreach (string buildDefinitionId in rolloutScorer.RepoConfig.BuildDefinitionIds)
            {
                // organization = rolloutScorer.RepoConfig.AzdoInstance
                // project = rolloutScorer.AzdoConfig.Project
                // definitions = buildDefinitionId
                // branchName = rolloutScorer.Branch
                // minTime = 
                // maxTime = 

                var azDOBuilds = await _buildHttpClient.GetBuildsAsync(
                    definitions: new []{ Convert.ToInt32(buildDefinitionId) }, 
                    minFinishTime: rolloutScorer.RolloutStartDate.UtcDateTime, 
                    maxFinishTime: rolloutScorer.RolloutEndDate.UtcDateTime, 
                    branchName: rolloutScorer.Branch);
                _logger.LogInformation($"Querying AzDO API");

                // No builds is a valid case (e.g. a failed rollout) and so the rest of the code can handle this
                // It still is potentially unexpected, so we're going to warn the user here
                if(azDOBuilds.Count == 0)
                {
                    _logger.LogWarning($"No builds were found for repo '{rolloutScorer.RepoConfig.Repo}' " +
                            $"(Build ID: '{buildDefinitionId}') during the specified dates ({rolloutScorer.RolloutStartDate} to {rolloutScorer.RolloutEndDate})");
                }

                foreach (Microsoft.TeamFoundation.Build.WebApi.Build build in azDOBuilds)
                {
                    // TODO: Convert Build into BuildSummary
                    rolloutScorer.BuildBreakdowns.Add(new Models.ScorecardBuildBreakdown(build.ToObject<Models.BuildSummary>()));
                }
            }
            rolloutScorer.BuildBreakdowns = rolloutScorer.BuildBreakdowns.OrderBy(x => x.BuildSummary.FinishTime).ToList();
            _logger.LogInformation($"Builds breakdowns created for: \n\t {string.Join(" \n\t ", rolloutScorer.BuildBreakdowns.Select(b => b?.BuildSummary?.WebLink ?? ""))}");

            await Task.WhenAll(rolloutScorer.BuildBreakdowns.Select(b => CollectStages(b, rolloutScorer)));
        }

        public async Task CollectStages(Models.ScorecardBuildBreakdown buildBreakdown, Models.RolloutScorer rolloutScorer)
        {
            

            string timelineLinkWithAttempts = $"{buildBreakdown.BuildSummary.TimelineLink}?changeId=1";
            _logger.LogInformation($"Querying AzDO API: {timelineLinkWithAttempts}");
            JObject jsonTimelineResponse = await GetAzdoApiResponseAsync(timelineLinkWithAttempts);
            Models.BuildTimeline timeline = jsonTimelineResponse.ToObject<Models.BuildTimeline>();

            if (timeline.Records != null)
            {
                // We're going to use this to store previous attempts as we find them
                Dictionary<string, Models.BuildTimeline> previousAttemptTimelines = new Dictionary<string, Models.BuildTimeline>();

                foreach (Models.BuildTimelineEntry record in timeline.Records)
                {
                    // We measure times at the stage level because this is the simplest thing to do
                    // By taking the min start time and max end time of all the stages except for the ones we exclude,
                    // we can determine a pretty accurate measurement of how long it took to rollout
                    if ((record.Type == "Checkpoint.Approval" || record.Type == "Stage") && !rolloutScorer.RepoConfig.ExcludeStages.Any(s => s == record.Name))
                    {
                        buildBreakdown.BuildSummary.Stages.Add(record);

                        if (record.PreviousAttempts.Count > 0)
                        {
                            // we're going to just add these attempts as additional stages
                            foreach (Models.PreviousAttempt attempt in record.PreviousAttempts)
                            {
                                if (!previousAttemptTimelines.ContainsKey(attempt.TimelineId))
                                {
                                    previousAttemptTimelines.Add(attempt.TimelineId,
                                        (await GetAzdoApiResponseAsync($"{buildBreakdown.BuildSummary.TimelineLink}/{attempt.TimelineId}")).ToObject<Models.BuildTimeline>());
                                }

                                if (previousAttemptTimelines[attempt.TimelineId] != null)
                                {
                                    buildBreakdown.BuildSummary.Stages.Add(previousAttemptTimelines[attempt.TimelineId].Records
                                        .Where(t => (t.Type == "Checkpoint.Approval" || t.Type == "Stage") && t.Name == record.Name).First());
                                }
                            }
                        }
                    }
                }

                if (buildBreakdown.BuildSummary.Stages.Any(s => s.Name.StartsWith("deploy", StringComparison.InvariantCultureIgnoreCase) && !string.IsNullOrEmpty(s.EndTime)))
                {
                    buildBreakdown.BuildSummary.DeploymentReached = true;
                    _logger.LogInformation($"Build {buildBreakdown.BuildSummary.BuildNumber} determined to have reached deployment.");
                }
                else
                {
                    _logger.LogInformation($"Build {buildBreakdown.BuildSummary.BuildNumber} determined to NOT have reached deployment.");
                }
            }
        }

        /// <summary>
        /// Calculates the "Time to Rollout" portion of the scorecard
        /// </summary>
        /// <returns>A timespan representing the total time to rollout and a list of scorecard build breakdowns for each rollout</returns>
        public TimeSpan CalculateTimeToRollout(Models.RolloutScorer rolloutScorer)
        {
            List<TimeSpan> rolloutBuildTimes = new List<TimeSpan>();

            // Loop over all the builds in the returned content and calculate start and end times
            foreach (Models.ScorecardBuildBreakdown build in rolloutScorer.BuildBreakdowns)
            {
                _logger.LogInformation($"Calculating time to rollout for build {build.BuildSummary.BuildNumber}");
                TimeSpan duration = GetPipelineDurationFromStages(build.BuildSummary.Stages);
                rolloutBuildTimes.Add(duration);
                build.Score.TimeToRollout = duration;
                _logger.LogInformation($"Build {build.BuildSummary.BuildNumber} determined to have rollout duration of {duration}.");
            }

            return new TimeSpan(rolloutBuildTimes.Sum(t => t.Ticks));
        }

        /// <summary>
        /// Calculates and returns the number of hotfixes and rollouts that occurred as part of the build
        /// </summary>
        /// <returns>The number of hotfixes and rollbacks which occurred as part of the rollout</returns>
        public async Task<(int numHotfixes, int numRollbacks)> CalculateNumHotfixesAndRollbacksFromAzdoAsync(Models.RolloutScorer rolloutScorer)
        {
            // Any attempt to figure out whether a deployment succeeded or failed will be inherently flawed
            // The process used here is as follows:
            //   * Find the first build where a deployment stage was reached; assume this means some sort of deployment happened
            //   * Every build after that also makes it to deployment (and is tagged "HOTFIX" when --assume-no-tags is not set) is a hotfix

            int numHotfixes = 0;
            int numRollbacks = 0;

            // This is a list of all builds that were part of this rollout which reached a deployment stage
            // We skip the first deployment because only things after that one can be hotfixes or rollbacks
            IEnumerable<Models.ScorecardBuildBreakdown> buildsToCheck = rolloutScorer.BuildBreakdowns.Where(b => b.BuildSummary.DeploymentReached).Skip(1);

            foreach (Models.ScorecardBuildBreakdown build in buildsToCheck)
            {
                Models.BuildSource source = (await GetAzdoApiResponseAsync(build.BuildSummary.SourceLink)).ToObject<Models.BuildSource>();

                // we can only automatically calculate rollbacks if they're tagged; so we specifically don't try when --assume-no-tags is passed
                if (!rolloutScorer.AssumeNoTags && source.Comment.Contains(AzureDevOpsCommitTags.RollbackTag, StringComparison.InvariantCultureIgnoreCase))
                {
                    numRollbacks++;
                    build.Score.Rollbacks = 1;
                    _logger.LogInformation($"Build {build.BuildSummary.BuildNumber} determined to be a ROLLBACK with commit message '{source.Comment}'");
                    _logger.LogInformation($"Web link: {build.BuildSummary.WebLink}");
                }
                // if we're assuming no tags, every deployment after the first is assumed to be a hotfix; otherwise we need to look specifically for the hotfix tag
                else if (rolloutScorer.AssumeNoTags || source.Comment.Contains(AzureDevOpsCommitTags.HotfixTag, StringComparison.InvariantCultureIgnoreCase))
                {
                    numHotfixes++;
                    build.Score.Hotfixes = 1;
                    _logger.LogInformation($"Build {build.BuildSummary.BuildNumber} determined to be a HOTFIX with commit message '{source.Comment}'");
                    _logger.LogInformation($"Web link: {build.BuildSummary.WebLink}");
                }
                // if none of these caught this deployment, then there's an untagged deployment when tags should be present; we'll warn the user about this
                else if (!source.Comment.Contains(AzureDevOpsCommitTags.RolloutTag, StringComparison.InvariantCultureIgnoreCase))
                {
                    _logger.LogInformation($"Untagged deployment found: build number '{build.BuildSummary.BuildNumber}' with commit message '{source.Comment}'");
                    _logger.LogInformation($"Web link: {build.BuildSummary.WebLink}");
                }
            }
            numHotfixes += rolloutScorer.ManualHotfixes;
            numRollbacks += rolloutScorer.ManualRollbacks;
            _logger.LogInformation($"Detected {numHotfixes} hotfixes ({rolloutScorer.ManualHotfixes} manual) and {numRollbacks} rollbacks ({rolloutScorer.ManualRollbacks} manual).");

            return (numHotfixes, numRollbacks);
        }

        public bool DetermineFailure(List<Octokit.Issue> githubIssues, Models.RolloutScorer rolloutScorer)
        {
            _logger.LogInformation($"Determining failure for {rolloutScorer.Repo}...");
            if (githubIssues.Any(i => _issueService.IssueContainsRelevantLabels(i, GithubLabelNames.FailureLabel, rolloutScorer.RepoConfig.GithubIssueLabel)))
            {
                _logger.LogInformation($"Issue with failure tag found for {rolloutScorer.Repo}; rollout marked as FAILED");
                return true;
            }

            if (rolloutScorer.BuildBreakdowns.Count == 0)
            {
                _logger.LogInformation($"No builds found for {rolloutScorer.Repo} this rollout; rollout marked as FAILED.");
                return true;
            }

            Models.ScorecardBuildBreakdown lastBuild = rolloutScorer.BuildBreakdowns.Last();
            _logger.LogInformation($"Last build is for {rolloutScorer.Repo} is {lastBuild.BuildSummary.BuildNumber} ({lastBuild.BuildSummary.WebLink})");

            if (lastBuild.Score.Rollbacks == 1)
            {
                _logger.LogInformation($"Last build ({lastBuild.BuildSummary.BuildNumber}) was a rollback; rollout marked as FAILED.");
                return true;
            }

            string lastBuildResult = lastBuild.BuildSummary.Result;
            _logger.LogInformation($"Build {lastBuild.BuildSummary.BuildNumber} has result '{lastBuildResult}'");
            switch (lastBuildResult)
            {
                case "succeeded":
                case "partiallySucceeded":
                    _logger.LogInformation($"Last build determined successful.");
                    return false;

                default:
                    _logger.LogInformation($"Last build determined unsuccessful; rollout marked as FAILED.");
                    return true;
            }
        }

        /// <summary>
        /// Calculates downtime from GitHub issues
        /// </summary>
        /// <param name="githubIssues">All GitHub issues associated with this rollout</param>
        /// <returns></returns>
        public async Task<TimeSpan> CalculateDowntimeAsync(List<Octokit.Issue> githubIssues, Models.RolloutScorer rolloutScorer)
        {
            TimeSpan downtime = TimeSpan.Zero;
            IEnumerable<Octokit.Issue> downtimeIssues = githubIssues
                .Where(i => _issueService.IssueContainsRelevantLabels(i, GithubLabelNames.DowntimeLabel, rolloutScorer.RepoConfig.GithubIssueLabel));

            foreach (Octokit.Issue issue in downtimeIssues)
            {
                _logger.LogInformation($"Determining downtime from issue {issue.Number}...");

                DateTimeOffset? downtimeStart;
                DateTimeOffset? downtimeFinish;

                // First, check the issue body
                _logger.LogInformation($"Checking issue body for downtime information...");
                downtimeStart = GetStartTimeFromIssueText(issue.Body, issue.HtmlUrl);
                WriteDowntimeDebugMessage(downtimeStart, "start");
                downtimeFinish = GetEndTimeFromIssueText(issue.Body, issue.HtmlUrl);
                WriteDowntimeDebugMessage(downtimeFinish, "finish");
                if (downtimeStart != null && downtimeFinish != null)
                {
                    downtime += (TimeSpan)(downtimeFinish - downtimeStart);
                    continue;
                }

                // Next, we're going to check all the comments on the issue
                _logger.LogInformation($"Checking issue comments for downtime information...");
                IEnumerable<IssueComment> comments = (await _githubClient.Issue.Comment
                    .GetAllForIssue(rolloutScorer.GithubConfig.ScorecardsGithubOrg, rolloutScorer.GithubConfig.ScorecardsGithubRepo, issue.Number));
                foreach (IssueComment comment in comments)
                {
                    _logger.LogInformation($"Checking comment at {comment.HtmlUrl} for downtime information...");
                    downtimeStart ??= GetStartTimeFromIssueText(comment.Body, comment.HtmlUrl);
                    WriteDowntimeDebugMessage(downtimeStart, "start");
                    downtimeFinish ??= GetEndTimeFromIssueText(comment.Body, comment.HtmlUrl);
                    WriteDowntimeDebugMessage(downtimeFinish, "finish");

                    if (downtimeStart != null && downtimeFinish != null)
                    {
                        break;
                    }
                }
                if (downtimeStart != null && downtimeFinish != null)
                {
                    downtime += (TimeSpan)(downtimeFinish - downtimeStart);
                    continue;
                }

                // If we haven't found both a start and end time yet, we're going to default to creation and/or close time
                _logger.LogInformation($"Downtime not yet found in body or comments; looking at issue start & close times...");
                downtimeStart ??= issue.CreatedAt;
                WriteDowntimeDebugMessage(downtimeStart, "start");
                downtimeFinish ??= issue.ClosedAt;
                WriteDowntimeDebugMessage(downtimeFinish, "finish");
                if (downtimeFinish == null)
                {
                    _logger.LogWarning($"Downtime issue was found unclosed and with no specified end time; " +
                        $"downtime calculation will be inaccurate (issue {issue.HtmlUrl})");
                    continue;
                }

                downtime += (TimeSpan)(downtimeFinish - downtimeStart);
            }

            _logger.LogInformation($"Downtime calculated as {downtime}.");
            return downtime;
        }

        private DateTimeOffset? GetStartTimeFromIssueText(string text, string issueUri)
        {
            return GetDateTimeOffsetFromIssueText(text, issueUri, @"start(?:ed)?");
        }
        private DateTimeOffset? GetEndTimeFromIssueText(string text, string issueUri)
        {
            return GetDateTimeOffsetFromIssueText(text, issueUri, @"(?:end|finish)(?:ed)?");
        }
        private DateTimeOffset? GetDateTimeOffsetFromIssueText(string text, string issueUri, string keyword)
        {
            // Regex matches the keyword followed by any number of spaces/colons;
            //   also allows for the word "at" followed by spaces after the keyword
            // Captures the following string until termination; should be a parseable datetime string or "now"
            // Terminates at semicolon, comma, or end of line
            // The Deployment Policy doc specifies a much stricter version of this pattern, but the regex captures
            //   the likely naturalistic variations of this pattern
            Regex keywordRegex = new Regex($@"{keyword}[: ]+(?:at[ ]+)?([^;,\n]+)(?:[;,]|$)",
                RegexOptions.IgnoreCase | RegexOptions.Multiline);
            Match match = keywordRegex.Match(text);
            if (match.Success)
            {
                string dateTimeString = match.Groups[1].Value;
                if (DateTimeOffset.TryParseExact(dateTimeString, "yyyy-MM-dd hh:mm zzz", CultureInfo.InvariantCulture.DateTimeFormat,
                    DateTimeStyles.AdjustToUniversal, out DateTimeOffset parsedDateTime))
                {
                    return parsedDateTime;
                }
                else
                {
                    _logger.LogWarning($"Failed to parse specified downtime in issue or comment {issueUri} (date/time string: '{dateTimeString}')");
                }
            }

            return null;
        }
        private void WriteDowntimeDebugMessage(DateTimeOffset? downtimeEvent, string downtimeEventDescription)
        {
            _logger.LogInformation($"Downtime {downtimeEventDescription} {downtimeEvent?.ToString() ?? "not"} found.");
        }

        /// <summary>
        /// Searches for issues in the appropriate GitHub repo by creation date and returns issues
        /// with string-tags in the title marking them as critical issues, hotfixes, and rollbacks
        /// </summary>
        /// <returns>A list of GitHub issues filed during the rollout window containing string-tags in the title marking them as critical issues, hotfixes, and/or rollbacks</returns>
        public async Task<List<Octokit.Issue>> GetRolloutIssuesFromGithubAsync(Models.RolloutScorer rolloutScorer)
        {
            _logger.LogInformation($"Getting rollout issues for {rolloutScorer.Repo} (searching issues in {rolloutScorer.GithubConfig.ScorecardsGithubOrg}/{rolloutScorer.GithubConfig.ScorecardsGithubRepo} "
                + $"from {rolloutScorer.RolloutStartDate} to {rolloutScorer.RolloutEndDate})");
            SearchIssuesRequest searchIssuesRequest = new SearchIssuesRequest
            {
                Created = new DateRange(rolloutScorer.RolloutStartDate, rolloutScorer.RolloutEndDate),
            };
            searchIssuesRequest.Repos.Add(rolloutScorer.GithubConfig.ScorecardsGithubOrg, rolloutScorer.GithubConfig.ScorecardsGithubRepo);

            List<Octokit.Issue> issueSearchResults = await GetAllIssuesFromSearchAsync(searchIssuesRequest);
            _logger.LogInformation($"Found {issueSearchResults.Count} issues in given time range.");

            return issueSearchResults.Where(issue =>
                _issueService.IssueContainsRelevantLabels(issue, GithubLabelNames.IssueLabel, rolloutScorer.RepoConfig.GithubIssueLabel) ||
                _issueService.IssueContainsRelevantLabels(issue, GithubLabelNames.HotfixLabel, rolloutScorer.RepoConfig.GithubIssueLabel) ||
                _issueService.IssueContainsRelevantLabels(issue, GithubLabelNames.RollbackLabel, rolloutScorer.RepoConfig.GithubIssueLabel) ||
                _issueService.IssueContainsRelevantLabels(issue, GithubLabelNames.DowntimeLabel, rolloutScorer.RepoConfig.GithubIssueLabel) ||
                _issueService.IssueContainsRelevantLabels(issue, GithubLabelNames.FailureLabel, rolloutScorer.RepoConfig.GithubIssueLabel)
                ).ToList();
        }

        private TimeSpan GetPipelineDurationFromStages(List<Models.BuildTimelineEntry> stages)
        {
            if (stages.Count == 0)
            {
                _logger.LogInformation($"Build had no stages; time to roll out logged as 0");
                return TimeSpan.Zero;
            }

            DateTimeOffset startTime = stages.Min(s =>
            {
                return DateTimeOffset.TryParse(s.StartTime, out DateTimeOffset start) ? start : DateTimeOffset.MaxValue;
            });
            _logger.LogInformation($"Build start time logged as {startTime}");
            DateTimeOffset endTime = stages.Max(s =>
            {
                return DateTimeOffset.TryParse(s.EndTime, out DateTimeOffset end) ? end : DateTimeOffset.MinValue;
            });
            _logger.LogInformation($"Build end time logged as {endTime}");

            TimeSpan approvalTime = stages
                .Where(s => s.Type == "Checkpoint.Approval")
                .Select(s => (DateTimeOffset.TryParse(s.StartTime, out DateTimeOffset start) && DateTimeOffset.TryParse(s.EndTime, out DateTimeOffset end)) ? end - start : TimeSpan.Zero)
                .Aggregate(TimeSpan.Zero, (l, r) => l + r);
            _logger.LogInformation($"Time spent waiting for approval calculated as {approvalTime}");

            TimeSpan duration = (endTime - startTime) - approvalTime;
            if (duration < TimeSpan.Zero)
            {
                _logger.LogWarning("Build time determined to be less than zero; reporting it as zero.");
            }

            return duration >= TimeSpan.Zero ? duration : TimeSpan.Zero;
        }

        /// <summary>
        /// Loops through paginated search results to get all issues
        /// </summary>
        /// <param name="searchIssuesRequest">A SearchIssuesRequest object representing the GitHub issue search</param>
        /// <returns>All the issues in the given search</returns>
        public async Task<List<Octokit.Issue>> GetAllIssuesFromSearchAsync(SearchIssuesRequest searchIssuesRequest)
        {
            SearchIssuesResult searchIssuesResult = await _githubClient.Search.SearchIssues(searchIssuesRequest);
            List<Octokit.Issue> issues = new List<Octokit.Issue>();
            issues.AddRange(searchIssuesResult.Items);

            // Loop through the pagination starting at page 2 since we've already done page 1 above
            for (int page = 2; issues.Count < searchIssuesResult.TotalCount; page++)
            {
                try
                {
                    searchIssuesRequest.Page = page;
                    searchIssuesResult = await _githubClient.Search.SearchIssues(searchIssuesRequest);
                    issues.AddRange(searchIssuesResult.Items);
                }
                catch (Exception e)
                {
                    _logger.LogWarning($"An exception occurred while attempting to paginate through the GitHub issues search API.");
                    _logger.LogWarning($"Attempting to access page {page} of search; currently read {issues.Count} of {searchIssuesResult.TotalCount}'");
                    _logger.LogWarning($"Exception: ${e.Message}");
                    break;
                }
            }

            return issues;
        }

        public void SetupHttpClient(string azdoPat)
        {
            _logger.LogInformation("Setting up HTTP client...");
            _httpClient.DefaultRequestHeaders.Accept.Clear();
            _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            _httpClient.DefaultRequestHeaders.UserAgent.Add(Program.GetProductInfoHeaderValue());
            _httpClient.DefaultRequestHeaders.Add("X-TFS-FedAuthRedirect", "Suppress");
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($":{azdoPat}")));
        }

        public void SetupGithubClient(string githubPat)
        {
            _logger.LogInformation("Setting up GitHub client...");
            _githubClient = _gitHubClientFactory.CreateGitHubClient(githubPat);
        }

        public async Task<JObject> GetAzdoApiResponseAsync(string apiRequest)
        {
            using (HttpResponseMessage response = await _httpClient.GetAsync(apiRequest))
            {
                if (response.StatusCode == HttpStatusCode.NoContent)
                {
                    return new JObject();
                }
                if (response.StatusCode == HttpStatusCode.Redirect)
                {
                    return await GetAzdoApiResponseAsync(Utilities.HandleApiRedirect(response, new Uri(apiRequest), Log));
                }
                response.EnsureSuccessStatusCode();
                return JObject.Parse(await response.Content.ReadAsStringAsync());
            }
        }
    }
}
