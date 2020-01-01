using Microsoft.Azure.KeyVault.Models;
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

        public async Task InitAsync()
        {
            // Convert the rollout start time and end time to the strings the AzDO API recognizes and fetch builds
            string rolloutStartTimeUriString = RolloutStartDate.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ");
            string rolloutEndTimeUriString = RolloutEndDate.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ");
            JObject responseContent = await GetAzdoApiResponseAsync($"https://dev.azure.com/{RepoConfig.AzdoInstance}/" +
                $"{AzdoConfig.Project}/_apis/build/builds?definitions={RepoConfig.DefinitionId}&branchName={Branch}" +
                $"&minTime={rolloutStartTimeUriString}&maxTime={rolloutEndTimeUriString}&api-version=5.1");

            // No builds is a valid case (e.g. a failed rollout) and so the rest of the code can handle this
            // It still is potentially unexpected, so we're going to warn the user here
            if (responseContent.Value<int>("count") == 0)
            {
                Utilities.WriteWarning($"No builds were found for repo '{RepoConfig.Repo}' " +
                    $"(Build ID: '{RepoConfig.DefinitionId}') during the specified dates ({RolloutStartDate} to {RolloutEndDate})");
            }

            JArray builds = responseContent.Value<JArray>("value");
            foreach (JToken build in builds)
            {
                BuildBreakdowns.Add(new ScorecardBuildBreakdown(build.ToObject<BuildSummary>()));
            }
        }

        /// <summary>
        /// Calculates the "Time to Rollout" portion of the scorecard
        /// </summary>
        /// <returns>A timespan representing the total time to rollout and a list of scorecard build breakdowns for each rollout</returns>
        public async Task<TimeSpan> CalculateTimeToRolloutAsync()
        {
            List<TimeSpan> rolloutBuildTimes = new List<TimeSpan>();

            // Loop over all the builds in the returned content and calculate start and end times
            foreach (ScorecardBuildBreakdown build in BuildBreakdowns)
            {
                TimeSpan duration = TimeSpan.Zero;
                string timelineLinkWithAttempts = $"{build.BuildSummary.TimelineLink}?changeId=1";
                JObject jsonTimelineResponse = await GetAzdoApiResponseAsync(timelineLinkWithAttempts);
                BuildTimeline timeline = jsonTimelineResponse.ToObject<BuildTimeline>();

                // We're going to use this to store previous attempts as we find them
                Dictionary<string, BuildTimeline> previousAttemptTimelines = new Dictionary<string, BuildTimeline>();

                if (timeline.Records != null)
                {
                    List<BuildTimelineEntry> stages = new List<BuildTimelineEntry>();
                    List<BuildTimelineEntry> approvalCheckpoints = new List<BuildTimelineEntry>();

                    foreach (BuildTimelineEntry record in timeline.Records)
                    {
                        // We measure times at the stage level because this is the simplest thing to do
                        // By taking the min start time and max end time of all the stages except for the ones we exclude,
                        // we can determine a pretty accurate measurement of how long it took to rollout
                        if ((record.Type == "Checkpoint.Approval" || record.Type == "Stage") && !RepoConfig.ExcludeStages.Any(s => s == record.Name))
                        {
                            stages.Add(record);

                            if (record.PreviousAttempts.Count > 0)
                            {
                                // we're going to just add these attempts as additional stages
                                foreach (PreviousAttempt attempt in record.PreviousAttempts)
                                {
                                    if (!previousAttemptTimelines.ContainsKey(attempt.TimelineId))
                                    {
                                        previousAttemptTimelines.Add(attempt.TimelineId,
                                            (await GetAzdoApiResponseAsync($"{build.BuildSummary.TimelineLink}/{attempt.TimelineId}")).ToObject<BuildTimeline>());
                                    }

                                    if (previousAttemptTimelines[attempt.TimelineId] != null)
                                    {
                                        stages.Add(previousAttemptTimelines[attempt.TimelineId].Records
                                            .Where(t => (t.Type == "Checkpoint.Approval" || t.Type == "Stage") && t.Name == record.Name).First());
                                    }
                                }
                            }
                        }
                    }
                    duration = GetPipelineDurationFromStages(stages);

                    if (stages.Any(s => s.Name.StartsWith("deploy", StringComparison.InvariantCultureIgnoreCase) && !string.IsNullOrEmpty(s.EndTime)))
                    {
                        build.BuildSummary.DeploymentReached = true;
                    }
                }
                rolloutBuildTimes.Add(duration);
                build.Score.TimeToRollout = duration;
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
                if (!AssumeNoTags && source.Comment.StartsWith(Utilities.RollbackAzureDevOpsTag, StringComparison.InvariantCultureIgnoreCase))
                {
                    numRollbacks++;
                    build.Score.Rollbacks = 1;
                }
                // if we're assuming no tags, every deployment after the first is assumed to be a hotfix; otherwise we need to look specifically for the hotfix tag
                else if (AssumeNoTags || source.Comment.StartsWith(Utilities.HotfixAzureDevOpsTag, StringComparison.InvariantCultureIgnoreCase))
                {
                    numHotfixes++;
                    build.Score.Hotfixes = 1;
                }
                // if none of these caught this deployment, then there's an untagged deployment when tags should be present; we'll warn the user about this
                else
                {
                    Utilities.WriteWarning($"Untagged deployment found: build number '{build.BuildSummary.BuildNumber}' with commit message '{source.Comment}'");
                    Utilities.WriteWarning($"Web link: {build.BuildSummary.WebLink}");
                }
            }
            numHotfixes += ManualHotfixes;
            numRollbacks += ManualRollbacks;

            return (numHotfixes, numRollbacks);
        }

        public bool DetermineFailure()
        {
            return BuildBreakdowns.Count == 0 || BuildBreakdowns.Last().BuildSummary.Result != "succeeded";
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
                .Where(i => Utilities.IssueContainsRelevantLabels(i, Utilities.DowntimeLabel, RepoConfig.GithubIssueLabel));

            foreach (Issue issue in downtimeIssues)
            {
                DateTimeOffset? downtimeStart;
                DateTimeOffset? downtimeFinish;

                // First, check the issue body
                downtimeStart = GetStartTimeFromIssueText(issue.Body, issue.HtmlUrl);
                downtimeFinish = GetEndTimeFromIssueText(issue.Body, issue.HtmlUrl);
                if (downtimeStart != null && downtimeFinish != null)
                {
                    downtime += (TimeSpan)(downtimeFinish - downtimeStart);
                    continue;
                }

                // Next, we're going to check all the comments on the issue
                IEnumerable<IssueComment> comments = (await _githubClient.Issue.Comment
                    .GetAllForIssue(GithubConfig.ScorecardsGithubOrg, GithubConfig.ScorecardsGithubRepo, issue.Number));
                foreach (IssueComment comment in comments)
                {
                    downtimeStart ??= GetStartTimeFromIssueText(comment.Body, comment.HtmlUrl);
                    downtimeFinish ??= GetEndTimeFromIssueText(comment.Body, comment.HtmlUrl);

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
                downtimeStart ??= issue.CreatedAt;
                downtimeFinish ??= issue.ClosedAt;
                if (downtimeFinish == null)
                {
                    Utilities.WriteWarning($"Downtime issue was found unclosed and with no specified end time; " +
                        $"downtime calculation will be inaccurate (issue {issue.HtmlUrl})");
                    continue;
                }

                downtime += (TimeSpan)(downtimeFinish - downtimeStart);
            }

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
                    Utilities.WriteWarning($"Failed to parse specified downtime in issue or comment {issueUri})");
                }
            }

            return null;
        }

        /// <summary>
        /// Searches for issues in the appropriate GitHub repo by creation date and returns issues
        /// with string-tags in the title marking them as critical issues, hotfixes, and rollbacks
        /// </summary>
        /// <returns>A list of GitHub issues filed during the rollout window containing string-tags in the title marking them as critical issues, hotfixes, and/or rollbacks</returns>
        public async Task<List<Issue>> GetRolloutIssuesFromGithubAsync()
        {
            SearchIssuesRequest searchIssuesRequest = new SearchIssuesRequest
            {
                Created = new DateRange(RolloutStartDate, RolloutEndDate),
            };
            searchIssuesRequest.Repos.Add(GithubConfig.ScorecardsGithubOrg, GithubConfig.ScorecardsGithubRepo);

            List<Issue> issueSearchResults = await GetAllIssuesFromSearchAsync(searchIssuesRequest);

            return issueSearchResults.Where(issue =>
                Utilities.IssueContainsRelevantLabels(issue, Utilities.IssueLabel, RepoConfig.GithubIssueLabel) ||
                Utilities.IssueContainsRelevantLabels(issue, Utilities.HotfixLabel, RepoConfig.GithubIssueLabel) ||
                Utilities.IssueContainsRelevantLabels(issue, Utilities.RollbackLabel, RepoConfig.GithubIssueLabel) ||
                Utilities.IssueContainsRelevantLabels(issue, Utilities.DowntimeLabel, RepoConfig.GithubIssueLabel)
                ).ToList();
        }

        private TimeSpan GetPipelineDurationFromStages(List<BuildTimelineEntry> stages)
        {
            DateTimeOffset startTime = stages.Min(s =>
            {
                return DateTimeOffset.TryParse(s.StartTime, out DateTimeOffset start) ? start : DateTimeOffset.MaxValue;
            });
            DateTimeOffset endTime = stages.Max(s =>
            {
                return DateTimeOffset.TryParse(s.EndTime, out DateTimeOffset end) ? end : DateTimeOffset.MinValue;
            });

            TimeSpan approvalTime = stages
                .Where(s => s.Type == "Checkpoint.Approval")
                .Select(s => (DateTimeOffset.TryParse(s.StartTime, out DateTimeOffset start) && DateTimeOffset.TryParse(s.EndTime, out DateTimeOffset end)) ? end - start : TimeSpan.Zero)
                .Aggregate(TimeSpan.Zero, (l, r) => l + r);

            return (endTime - startTime) - approvalTime;
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
                    Utilities.WriteWarning($"An exception occurred while attempting to paginate through the GitHub issues search API.");
                    Utilities.WriteWarning($"Attempting to access page {page} of search; currently read {issues.Count} of {searchIssuesResult.TotalCount}'");
                    Utilities.WriteWarning($"Exception: ${e.Message}");
                    break;
                }
            }

            return issues;
        }

        public void SetupHttpClient(SecretBundle azdoPat)
        {
            _httpClient.DefaultRequestHeaders.Accept.Clear();
            _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            _httpClient.DefaultRequestHeaders.UserAgent.Add(Utilities.GetProductInfoHeaderValue());
            _httpClient.DefaultRequestHeaders.Add("X-TFS-FedAuthRedirect", "Suppress");
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($":{azdoPat.Value}")));
        }

        public void SetupGithubClient(SecretBundle githubPat)
        {
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
                    return await HandleApiRedirect(response, new Uri(apiRequest));
                }
                response.EnsureSuccessStatusCode();
                return JObject.Parse(await response.Content.ReadAsStringAsync());
            }
        }

        public async Task<JObject> HandleApiRedirect(HttpResponseMessage redirect, Uri apiRequest)
        {
            // Since the API will sometimes 302 us, we're going to do a quick check to see
            // that we're still being sent to AzDO and not some random location
            // If so, we'll provide our auth so we don't get 401'd
            Uri redirectUri = redirect.Headers.Location;
            if (redirectUri.Scheme.ToLower() != "https")
            {
                Utilities.WriteError($"API attempted to redirect to using incorrect scheme (expected 'https', was '{redirectUri.Scheme}'");
                Utilities.WriteError($"Request URI: '{apiRequest}'\nRedirect URI: '{redirectUri}'");
                throw new HttpRequestException("Bad redirect scheme");
            }
            else if (redirectUri.Host != apiRequest.Host)
            {
                Utilities.WriteError($"API attempted to redirect to unknown host '{redirectUri.Host}' (expected '{apiRequest.Host}'); not passing auth parameters");
                Utilities.WriteError($"Request URI: '{apiRequest}'\nRedirect URI: '{redirectUri}'");
                throw new HttpRequestException("Bad redirect host");
            }
            else
            {
                return await GetAzdoApiResponseAsync(redirectUri.ToString());
            }
        }
    }
}
