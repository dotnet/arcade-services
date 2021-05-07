// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.GitHub.Authentication;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SubscriptionActorService
{
    public class PullRequestPolicyFailureNotifier : IPullRequestPolicyFailureNotifier
    {
        public ILogger<PullRequestPolicyFailureNotifier> Logger { get; }
        public IRemoteFactory DarcRemoteFactory { get; }
        public IGitHubTokenProvider GitHubTokenProvider { get; }
        public IGitHubClientFactory GitHubClientFactory { get; }

        public PullRequestPolicyFailureNotifier(
            IGitHubTokenProvider gitHubTokenProvider,
            IGitHubClientFactory gitHubClientFactory,
            IRemoteFactory darcFactory,
            ILogger<PullRequestPolicyFailureNotifier> logger)
        {
            Logger = logger;
            GitHubTokenProvider = gitHubTokenProvider;
            GitHubClientFactory = gitHubClientFactory;
            DarcRemoteFactory = darcFactory;
        }

        public async Task TagSourceRepositoryGitHubContactsAsync(InProgressPullRequest pr)
        {
            // We'll try to notify the source repo if the subscription provided a list of aliases to tag.
            // The API checks when creating / updating subscriptions that any resolve-able logins are in the
            // "Microsoft" Github org, so we can safely use them in any comment.
            if (pr.SourceRepoNotified == true)
            {
                Logger.LogInformation($"Skipped notifying source repository for {pr.Url}'s failed policies, as it has already been tagged");
                return;
            }

            // This should only ever happen for non-batched subscriptions, so if there are multiple or no subscriptions going in here, we want to throw and fail.
            var subscriptionFromPr = pr.ContainedSubscriptions.Single();
            var darcRemote = await DarcRemoteFactory.GetBarOnlyRemoteAsync(Logger);
            var darcSubscriptionObject = await darcRemote.GetSubscriptionAsync(subscriptionFromPr.SubscriptionId.ToString());
            string sourceRepository = darcSubscriptionObject.SourceRepository;
            string targetRepository = darcSubscriptionObject.TargetRepository;

            (string owner, string repo, int prIssueId) = GitHubClient.ParsePullRequestUri(pr.Url);

            List<string> tagsToNotify = new List<string>();
            if (!string.IsNullOrEmpty(darcSubscriptionObject.PullRequestFailureNotificationTags))
            {
                tagsToNotify.AddRange(darcSubscriptionObject.PullRequestFailureNotificationTags.Split(';', StringSplitOptions.RemoveEmptyEntries));
            }

            if (tagsToNotify.Count == 0)
            {
                Logger.LogInformation("Found no matching tags for source '{sourceRepo}' to target '{targetRepo}' on channel '{channel}'. ", sourceRepository, targetRepository, darcSubscriptionObject.Channel);
                return;
            }

            // At this point we definitely have notifications to make, so do it.
            Logger.LogInformation("Found {count} matching tags for source '{sourceRepo}' to target '{targetRepo}' on channel '{channel}'. ", tagsToNotify.Count, sourceRepository, targetRepository, darcSubscriptionObject.Channel);

            // To ensure GitHub notifies the people / teams on the list, forcibly check they are inserted with a preceding '@'
            for (int i = 0; i < tagsToNotify.Count; i++)
            {
                if (!tagsToNotify[i].StartsWith('@'))
                {
                    tagsToNotify[i] = $"@{tagsToNotify[i]}";
                }
            }

            string githubToken = await GitHubTokenProvider.GetTokenForRepository(targetRepository);
            var gitHubClient = GitHubClientFactory.CreateGitHubClient(githubToken);

            string sourceRepoNotificationComment = @$"
#### Notification for subscribed users from {sourceRepository}:

{string.Join($", {Environment.NewLine}", tagsToNotify)}

#### Action requested: Please take a look at this failing automated dependency-flow pull request's checks; failures may be related to changes which originated in your repo.

- This pull request contains changes from your source repo ({sourceRepository}) and seems to have failed checks in this PR.  Please take a peek at the failures and comment if they seem relevant to your changes.
- If you're being tagged in this comment it is due to an entry in the related Maestro Subscription of the Build Asset Registry.  If you feel this entry has added your GitHub login or your GitHub team in error, please update the subscription to reflect this.
- For more details, please read [the Arcade Darc documentation](https://github.com/dotnet/arcade/blob/main/Documentation/Darc.md#update-subscription)
";
            await gitHubClient.Issue.Comment.Create(owner, repo, prIssueId, sourceRepoNotificationComment);
            pr.SourceRepoNotified = true;
        }
    }
}
