// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Maestro.Data;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Octokit;
using Xunit;

namespace DependencyUpdateErrorProcessor.Tests
{
    public class UpdateGithubIssueTest : DependencyUpdateErrorProcessorTests
    {
        private const string SubscriptionId = "00000000-0000-0000-0000-000000000001";
        private const string MethodName = "UpdateAssetsAsync";
        private const string ErrorMessage = "build: 14	Unexpected error processing action: Validation Failed";
        private const string Branch = "BranchOne";
        private const string RepoUrl = "https://github.test/test-org-1/test-repo-1";
        private const int RepoId = 0;
        private const int IssueNumber = 1;
        [Fact]
        public async Task CreateIssueAndUpdateIssueBody()
        {
            RepositoryBranchUpdateHistoryEntry firstError =
                new RepositoryBranchUpdateHistoryEntry
                {
                    Repository = RepoUrl,
                    Branch = Branch,
                    Method = MethodName,
                    Timestamp = new DateTime(2200, 1, 1),
                    Arguments = $"[\"{SubscriptionId}\",\"{MethodName}\",\"{ErrorMessage}\"]",
                    Success = false,
                    ErrorMessage = ErrorMessage,
                    Action = "Creating new issue"
                };

            RepositoryBranchUpdateHistoryEntry secondError =
                new RepositoryBranchUpdateHistoryEntry
                {
                    Repository = RepoUrl,
                    Branch = Branch,
                    Method = MethodName,
                    Timestamp = new DateTime(2200, 2, 1),
                    Arguments = $"[\"{SubscriptionId}\",\"{MethodName}\",\"{ErrorMessage}\"]",
                    Success = false,
                    ErrorMessage = ErrorMessage,
                    Action = "Updating existing issue",
                };
            Context.RepoBranchUpdateInMemory = new List<RepositoryBranchUpdateHistoryEntry>
                {firstError , secondError};
            Mock<Maestro.Data.Models.Subscription> subscription = new Mock<Maestro.Data.Models.Subscription>();
            subscription.Object.Id = Guid.Parse(SubscriptionId);
            subscription.Object.SourceRepository = "Source Repo";
            subscription.Object.TargetRepository = "Target Repo";
            Context.Subscriptions.Add(subscription.Object);
            Context.SaveChanges();

            var repository = new Repository();

            GithubClient.Setup(x => x.Repository.Get(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(repository);
            Issue issueCreated = GetIssue();
            List<NewIssue> newIssues = new List<NewIssue>();
            GithubClient.Setup(x => x.Issue.Create(It.IsAny<long>(), Capture.In(newIssues))).ReturnsAsync(issueCreated);
            GithubClient.Setup(x => x.Issue.Get(RepoId, IssueNumber)).ReturnsAsync(issueCreated);
            List<IssueUpdate> issueUpdate = new List<IssueUpdate>();
            GithubClient.Setup(x => x.Issue.Update(RepoId, IssueNumber, Capture.In(issueUpdate))).ReturnsAsync(issueCreated);
            DependencyUpdateErrorProcessor errorProcessor =
                ActivatorUtilities.CreateInstance<DependencyUpdateErrorProcessor>(Scope.ServiceProvider,
                    Context);
            await errorProcessor.ProcessDependencyUpdateErrorsAsync();

            Assert.Single(newIssues);
            Assert.Contains(RepoUrl, newIssues[0].Title);
            Assert.Equal("DependencyUpdateError", newIssues[0].Labels[0]);
            Assert.Contains(SubscriptionId, newIssues[0].Body);
            Assert.Contains(MethodName, newIssues[0].Body);
            Assert.Contains(RepoUrl, newIssues[0].Body);
            Assert.Contains("1/1/2200 12:00:00 AM", newIssues[0].Body);
            Assert.Contains(Branch, newIssues[0].Body);
            Assert.Contains(
                $"[marker]: <> (subscriptionId: '{SubscriptionId}', method: '{MethodName}', errorMessage: '{ErrorMessage}')",
                newIssues[0].Body);

        }

        public Issue GetIssue()
        {
            Issue issue = new Issue
            (
                "testUrl",
                "testHtml",
                "testCommentUrl",
                "testEvents",
                1,
                ItemState.Open,
                "testTitle",
                $"[marker]: <> (subscriptionId: '{SubscriptionId}', method: '{MethodName}', errorMessage: '{ErrorMessage}')",
                null,
                null,
                null,
                null,
                null,
                null,
                1,
                null,
                null,
                new DateTime(2020, 01, 01),
                null,
                12,
                "test",
                false,
                null,
                null
            );
            return issue;
        }
    }
}
