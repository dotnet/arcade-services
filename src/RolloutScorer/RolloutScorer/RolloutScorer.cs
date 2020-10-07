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

namespace RolloutScorer
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

        // The AzDO API has some 302s in it that break auth so we have to handle those ourselves
        private readonly HttpClient _httpClient = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false });
        private GitHubClient _githubClient;

        public ILogger Log { get; set; }
        public LogLevel LogLevel { get; set; } = LogLevel.Information;

        public async Task InitAsync()
        {
            // Convert the rollout start time and end time to the strings the AzDO API recognizes and fetch builds
            string rolloutStartTimeUriString = RolloutStartDate.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ");
            string rolloutEndTimeUriString = RolloutEndDate.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ");

            foreach (string buildDefinitionId in RepoConfig.BuildDefinitionIds)
            {
                string azdoQuery = $"https://dev.azure.com/{RepoConfig.AzdoInstance}/" +
                    $"{AzdoConfig.Project}/_apis/build/builds?definitions={buildDefinitionId}&branchName={Branch}" +
                    $"&minTime={rolloutStartTimeUriString}&maxTime={rolloutEndTimeUriString}&api-version=5.1";
                Utilities.WriteDebug($"Querying AzDO API: {azdoQuery}", Log, LogLevel);
                JObject responseContent = await GetAzdoApiResponseAsync(azdoQuery);

                // No builds is a valid case (e.g. a failed rollout) and so the rest of the code can handle this
                // It still is potentially unexpected, so we're going to warn the user here
                if (responseContent.Value<int>("count") == 0)
                {
                    Utilities.WriteWarning($"No builds were found for repo '{RepoConfig.Repo}' " +
                            $"(Build ID: '{buildDefinitionId}') during the specified dates ({RolloutStartDate} to {RolloutEndDate})", Log);
                }

                JArray builds = responseContent.Value<JArray>("value");
                foreach (JToken build in builds)
                {
                    BuildBreakdowns.Add(new ScorecardBuildBreakdown(build.ToObject<BuildSummary>()));
                }
            }
            BuildBreakdowns = BuildBreakdowns.OrderBy(x => x.BuildSummary.FinishTime).ToList();
            Utilities.WriteDebug($"Builds breakdowns created for: \n\t {string.Join(" \n\t ", BuildBreakdowns.Select(b => b?.BuildSummary?.WebLink ?? ""))}", Log, LogLevel);

            await Task.WhenAll(BuildBreakdowns.Select(b => CollectStages(b)));
        }

        public async Task CollectStages(ScorecardBuildBreakdown buildBreakdown)
        {
            string timelineLinkWithAttempts = $"{buildBreakdown.BuildSummary.TimelineLink}?changeId=1";
            Utilities.WriteDebug($"Querying AzDO API: {timelineLinkWithAttempts}", Log, LogLevel);
            JObject jsonTimelineResponse = await GetAzdoApiResponseAsync(timelineLinkWithAttempts);
            BuildTimeline timeline = jsonTimelineResponse.ToObject<BuildTimeline>();

            if (timeline.Records != null)
            {
                // We're going to use this to store previous attempts as we find them
                Dictionary<string, BuildTimeline> previousAttemptTimelines = new Dictionary<string, BuildTimeline>();

                foreach (BuildTimelineEntry record in timeline.Records)
                {
                    // We measure times at the stage level because this is the simplest thing to do
                    // By taking the min start time and max end time of all the stages except for the ones we exclude,
                    // we can determine a pretty accurate measurement of how long it took to rollout
                    if ((record.Type == "Checkpoint.Approval" || record.Type == "Stage") && !RepoConfig.ExcludeStages.Any(s => s == record.Name))
                    {
                        buildBreakdown.BuildSummary.Stages.Add(record);

                        if (record.PreviousAttempts.Count > 0)
                        {
                            // we're going to just add these attempts as additional stages
                            foreach (PreviousAttempt attempt in record.PreviousAttempts)
                            {
                                if (!previousAttemptTimelines.ContainsKey(attempt.TimelineId))
                                {
                                    previousAttemptTimelines.Add(attempt.TimelineId,
                                        (await GetAzdoApiResponseAsync($"{buildBreakdown.BuildSummary.TimelineLink}/{attempt.TimelineId}")).ToObject<BuildTimeline>());
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
                    Utilities.WriteDebug($"Build {buildBreakdown.BuildSummary.BuildNumber} determined to have reached deployment.", Log, LogLevel);
                }
                else
                {
                    Utilities.WriteDebug($"Build {buildBreakdown.BuildSummary.BuildNumber} determined to NOT have reached deployment.", Log, LogLevel);
                }
            }
        }

        /// <summary>
        /// Calculates the "Time to Rollout" portion of the scorecard
        /// </summary>
        /// <returns>A timespan representing the total time to rollout and a list of scorecard build breakdowns for each rollout</returns>
        public TimeSpan CalculateTimeToRollout()
        {
            List<TimeSpan> rolloutBuildTimes = new List<TimeSpan>();

            // Loop over all the builds in the returned content and calculate start and end times
            foreach (ScorecardBuildBreakdown build in BuildBreakdowns)
            {
                Utilities.WriteDebug($"Calculating time to rollout for build {build.BuildSummary.BuildNumber}", Log, LogLevel);
                TimeSpan duration = GetPipelineDurationFromStages(build.BuildSummary.Stages);
                rolloutBuildTimes.Add(duration);
                build.Score.TimeToRollout = duration;
                Utilities.WriteDebug($"Build {build.BuildSummary.BuildNumber} determined to have rollout duration of {duration}.", Log, LogLevel);
            }

            return new TimeSpan(rolloutBuildTimes.Sum(t => t.Ticks));
        }

        /// <summary>
        /// Calculates and returns the number of hotfixes and rollouts that occurred as part of the build
        /// </summary>
        /// <returns>The number of hotfixes and rollbacks which occurred as part of the rollout</returns>
        public async Task<(int numHotfixes, int numRollbacks)> CalculateNumHotfixesAndRollbacksFromAzdoAsync()
        {
            // Any attempt to figure out whether a deployment succeeded or failed will be inherently flawed
            // The process used here is as follows:
            //   * Find the first build where a deployment stage was reached; assume this means some sort of deployment happened
            //   * Every build after that also makes it to deployment (and is tagged "HOTFIX" when --assume-no-tags is not set) is a hotfix

            int numHotfixes = 0;
            int numRollbacks = 0;

            // This is a list of all builds that were part of this rollout which reached a deployment stage
            // We skip the first deployment because only things after that one can be hotfixes or rollbacks
            IEnumerable<ScorecardBuildBreakdown> buildsToCheck = BuildBreakdowns.Where(b => b.BuildSummary.DeploymentReached).Skip(1);

            foreach (ScorecardBuildBreakdown build in buildsToCheck)
            {
                BuildSource source = (await GetAzdoApiResponseAsync(build.BuildSummary.SourceLink)).ToObject<BuildSource>();

                // we can only automatically calculate rollbacks if they're tagged; so we specifically don't try when --assume-no-tags is passed
                if (!AssumeNoTags && source.Comment.Contains(AzureDevOpsCommitTags.RollbackTag, StringComparison.InvariantCultureIgnoreCase))
                {
                    numRollbacks++;
                    build.Score.Rollbacks = 1;
                    Utilities.WriteDebug($"Build {build.BuildSummary.BuildNumber} determined to be a ROLLBACK with commit message '{source.Comment}'", Log, LogLevel);
                    Utilities.WriteDebug($"Web link: {build.BuildSummary.WebLink}", Log, LogLevel);
                }
                // if we're assuming no tags, every deployment after the first is assumed to be a hotfix; otherwise we need to look specifically for the hotfix tag
                else if (AssumeNoTags || source.Comment.Contains(AzureDevOpsCommitTags.HotfixTag, StringComparison.InvariantCultureIgnoreCase))
                {
                    numHotfixes++;
                    build.Score.Hotfixes = 1;
                    Utilities.WriteDebug($"Build {build.BuildSummary.BuildNumber} determined to be a HOTFIX with commit message '{source.Comment}'", Log, LogLevel);
                    Utilities.WriteDebug($"Web link: {build.BuildSummary.WebLink}", Log, LogLevel);
                }
                // if none of these caught this deployment, then there's an untagged deployment when tags should be present; we'll warn the user about this
                else if (!source.Comment.Contains(AzureDevOpsCommitTags.RolloutTag, StringComparison.InvariantCultureIgnoreCase))
                {
                    Utilities.WriteWarning($"Untagged deployment found: build number '{build.BuildSummary.BuildNumber}' with commit message '{source.Comment}'", Log);
                    Utilities.WriteWarning($"Web link: {build.BuildSummary.WebLink}", Log);
                }
            }
            numHotfixes += ManualHotfixes;
            numRollbacks += ManualRollbacks;
            Utilities.WriteDebug($"Detected {numHotfixes} hotfixes ({ManualHotfixes} manual) and {numRollbacks} rollbacks ({ManualRollbacks} manual).", Log, LogLevel);

            return (numHotfixes, numRollbacks);
        }

        public bool DetermineFailure(List<Issue> githubIssues)
        {
            Utilities.WriteDebug($"Determining failure for {Repo}...", Log, LogLevel);
            if (githubIssues.Any(i => Utilities.IssueContainsRelevantLabels(i, GithubLabelNames.FailureLabel, RepoConfig.GithubIssueLabel, Log, LogLevel)))
            {
                Utilities.WriteDebug($"Issue with failure tag found for {Repo}; rollout marked as FAILED", Log, LogLevel);
                return true;
            }

            if (BuildBreakdowns.Count == 0)
            {
                Utilities.WriteDebug($"No builds found for {Repo} this rollout; rollout marked as FAILED.", Log, LogLevel);
                return true;
            }

            ScorecardBuildBreakdown lastBuild = BuildBreakdowns.Last();
            Utilities.WriteDebug($"Last build is for {Repo} is {lastBuild.BuildSummary.BuildNumber} ({lastBuild.BuildSummary.WebLink})", Log, LogLevel);

            if (lastBuild.Score.Rollbacks == 1)
            {
                Utilities.WriteDebug($"Last build ({lastBuild.BuildSummary.BuildNumber}) was a rollback; rollout marked as FAILED.", Log, LogLevel);
                return true;
            }

            string lastBuildResult = lastBuild.BuildSummary.Result;
            Utilities.WriteDebug($"Build {lastBuild.BuildSummary.BuildNumber} has result '{lastBuildResult}'", Log, LogLevel);
            switch (lastBuildResult)
            {
                case "succeeded":
                case "partiallySucceeded":
                    Utilities.WriteDebug($"Last build determined successful.", Log, LogLevel);
                    return false;

                default:
                    Utilities.WriteDebug($"Last build determined unsuccessful; rollout marked as FAILED.", Log, LogLevel);
                    return true;
            }
        }

        /// <summary>
        /// Calculates downtime from GitHub issues
        /// </summary>
        /// <param name="githubIssues">All GitHub issues associated with this rollout</param>
        /// <returns></returns>
        public async Task<TimeSpan> CalculateDowntimeAsync(List<Issue> githubIssues)
        {
            TimeSpan downtime = TimeSpan.Zero;
            IEnumerable<Issue> downtimeIssues = githubIssues
                .Where(i => Utilities.IssueContainsRelevantLabels(i, GithubLabelNames.DowntimeLabel, RepoConfig.GithubIssueLabel, Log, LogLevel));

            foreach (Issue issue in downtimeIssues)
            {
                Utilities.WriteDebug($"Determining downtime from issue {issue.Number}...", Log, LogLevel);

                DateTimeOffset? downtimeStart;
                DateTimeOffset? downtimeFinish;

                // First, check the issue body
                Utilities.WriteDebug($"Checking issue body for downtime information...", Log, LogLevel);
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
                Utilities.WriteDebug($"Checking issue comments for downtime information...", Log, LogLevel);
                IEnumerable<IssueComment> comments = (await _githubClient.Issue.Comment
                    .GetAllForIssue(GithubConfig.ScorecardsGithubOrg, GithubConfig.ScorecardsGithubRepo, issue.Number));
                foreach (IssueComment comment in comments)
                {
                    Utilities.WriteDebug($"Checking comment at {comment.HtmlUrl} for downtime information...", Log, LogLevel);
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
                Utilities.WriteDebug($"Downtime not yet found in body or comments; looking at issue start & close times...", Log, LogLevel);
                downtimeStart ??= issue.CreatedAt;
                WriteDowntimeDebugMessage(downtimeStart, "start");
                downtimeFinish ??= issue.ClosedAt;
                WriteDowntimeDebugMessage(downtimeFinish, "finish");
                if (downtimeFinish == null)
                {
                    Utilities.WriteWarning($"Downtime issue was found unclosed and with no specified end time; " +
                        $"downtime calculation will be inaccurate (issue {issue.HtmlUrl})", Log);
                    continue;
                }

                downtime += (TimeSpan)(downtimeFinish - downtimeStart);
            }

            Utilities.WriteDebug($"Downtime calculated as {downtime}.", Log, LogLevel);
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
                    Utilities.WriteWarning($"Failed to parse specified downtime in issue or comment {issueUri} (date/time string: '{dateTimeString}')", Log);
                }
            }

            return null;
        }
        private void WriteDowntimeDebugMessage(DateTimeOffset? downtimeEvent, string downtimeEventDescription)
        {
            Utilities.WriteDebug($"Downtime {downtimeEventDescription} {downtimeEvent?.ToString() ?? "not"} found.", Log, LogLevel);
        }

        /// <summary>
        /// Searches for issues in the appropriate GitHub repo by creation date and returns issues
        /// with string-tags in the title marking them as critical issues, hotfixes, and rollbacks
        /// </summary>
        /// <returns>A list of GitHub issues filed during the rollout window containing string-tags in the title marking them as critical issues, hotfixes, and/or rollbacks</returns>
        public async Task<List<Issue>> GetRolloutIssuesFromGithubAsync()
        {
            Utilities.WriteDebug($"Getting rollout issues for {Repo} (searching issues in {GithubConfig.ScorecardsGithubOrg}/{GithubConfig.ScorecardsGithubRepo} "
                + $"from {RolloutStartDate} to {RolloutEndDate})", Log, LogLevel);
            SearchIssuesRequest searchIssuesRequest = new SearchIssuesRequest
            {
                Created = new DateRange(RolloutStartDate, RolloutEndDate),
            };
            searchIssuesRequest.Repos.Add(GithubConfig.ScorecardsGithubOrg, GithubConfig.ScorecardsGithubRepo);

            List<Issue> issueSearchResults = await GetAllIssuesFromSearchAsync(searchIssuesRequest);
            Utilities.WriteDebug($"Found {issueSearchResults.Count} issues in given time range.", Log, LogLevel);

            return issueSearchResults.Where(issue =>
                Utilities.IssueContainsRelevantLabels(issue, GithubLabelNames.IssueLabel, RepoConfig.GithubIssueLabel, Log) ||
                Utilities.IssueContainsRelevantLabels(issue, GithubLabelNames.HotfixLabel, RepoConfig.GithubIssueLabel, Log, LogLevel) ||
                Utilities.IssueContainsRelevantLabels(issue, GithubLabelNames.RollbackLabel, RepoConfig.GithubIssueLabel, Log, LogLevel) ||
                Utilities.IssueContainsRelevantLabels(issue, GithubLabelNames.DowntimeLabel, RepoConfig.GithubIssueLabel, Log, LogLevel) ||
                Utilities.IssueContainsRelevantLabels(issue, GithubLabelNames.FailureLabel, RepoConfig.GithubIssueLabel, Log, LogLevel)
                ).ToList();
        }

        private TimeSpan GetPipelineDurationFromStages(List<BuildTimelineEntry> stages)
        {
            if (stages.Count == 0)
            {
                Utilities.WriteTrace($"Build had no stages; time to roll out logged as 0", Log, LogLevel);
                return TimeSpan.Zero;
            }   
            
            DateTimeOffset startTime = stages.Min(s =>
            {
                return DateTimeOffset.TryParse(s.StartTime, out DateTimeOffset start) ? start : DateTimeOffset.MaxValue;
            });
            Utilities.WriteTrace($"Build start time logged as {startTime}", Log, LogLevel);
            DateTimeOffset endTime = stages.Max(s =>
            {
                return DateTimeOffset.TryParse(s.EndTime, out DateTimeOffset end) ? end : DateTimeOffset.MinValue;
            });
            Utilities.WriteTrace($"Build end time logged as {endTime}", Log, LogLevel);

            TimeSpan approvalTime = stages
                .Where(s => s.Type == "Checkpoint.Approval")
                .Select(s => (DateTimeOffset.TryParse(s.StartTime, out DateTimeOffset start) && DateTimeOffset.TryParse(s.EndTime, out DateTimeOffset end)) ? end - start : TimeSpan.Zero)
                .Aggregate(TimeSpan.Zero, (l, r) => l + r);
            Utilities.WriteTrace($"Time spent waiting for approval calculated as {approvalTime}", Log, LogLevel);

            TimeSpan duration = (endTime - startTime) - approvalTime;
            if (duration < TimeSpan.Zero)
            {
                Utilities.WriteWarning("Build time determined to be less than zero; reporting it as zero.", Log);
            }

            return duration >= TimeSpan.Zero ? duration : TimeSpan.Zero;
        }

        /// <summary>
        /// Loops through paginated search results to get all issues
        /// </summary>
        /// <param name="searchIssuesRequest">A SearchIssuesRequest object representing the GitHub issue search</param>
        /// <returns>All the issues in the given search</returns>
        public async Task<List<Issue>> GetAllIssuesFromSearchAsync(SearchIssuesRequest searchIssuesRequest)
        {
            SearchIssuesResult searchIssuesResult = await _githubClient.Search.SearchIssues(searchIssuesRequest);
            List<Issue> issues = new List<Issue>();
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
                    Utilities.WriteWarning($"An exception occurred while attempting to paginate through the GitHub issues search API.", Log);
                    Utilities.WriteWarning($"Attempting to access page {page} of search; currently read {issues.Count} of {searchIssuesResult.TotalCount}'", Log);
                    Utilities.WriteWarning($"Exception: ${e.Message}", Log);
                    break;
                }
            }

            return issues;
        }

        public void SetupHttpClient(string azdoPat)
        {
            Utilities.WriteInformation("Setting up HTTP client...", Log);
            _httpClient.DefaultRequestHeaders.Accept.Clear();
            _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            _httpClient.DefaultRequestHeaders.UserAgent.Add(Program.GetProductInfoHeaderValue());
            _httpClient.DefaultRequestHeaders.Add("X-TFS-FedAuthRedirect", "Suppress");
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($":{azdoPat}")));
        }

        public void SetupGithubClient(string githubPat)
        {
            Utilities.WriteInformation("Setting up GitHub client...", Log);
            _githubClient = Utilities.GetGithubClient(githubPat);
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
