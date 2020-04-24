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
        private const int CommentId = 1;
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
            //Shared mocks works for Xunit as it creates a separate file for each test, but for Nunit there will be a conflict. 
            //We need to take care of this if we port to Nunit in future.
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

        [Fact]
        public async Task CreateIssueAndAddAdditionalComment()
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
                    Method = "ProcessPendingUpdatesAsync",
                    Timestamp = new DateTime(2200, 2, 1),
                    Arguments = "ProcessPendingUpdatesAsync error",
                    Success = false,
                    ErrorMessage = ErrorMessage,
                    Action = "Create a new issue comment",
                };

            Context.RepoBranchUpdateInMemory = new List<RepositoryBranchUpdateHistoryEntry>
                {firstError , secondError};
            Maestro.Data.Models.Subscription subscription = new Maestro.Data.Models.Subscription
            {
                Id = Guid.Parse(SubscriptionId),
                SourceRepository = "Source Repo",
                TargetRepository = "Target Repo",
            };
            Context.Subscriptions.Add(subscription);
            Context.SaveChanges();
            Repository repository = new Repository();
            GithubClient.Setup(x => x.Repository.Get(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(repository);
            Issue updateIssue = GetIssue();
            Octokit.AuthorAssociation author = new Octokit.AuthorAssociation();
            string nodeId = "1";
            IssueComment comment = new IssueComment
            (
                1,
                nodeId,
                "Url",
                "htmlUrl",
                "New comment for the existing issue",
                new DateTime(2200, 02, 02),
                new DateTime(2200, 03, 01),
                new User(), 
                new ReactionSummary(), 
                author);
            List<IssueComment> issueComment = new List<IssueComment> { comment };
            List<NewIssue> newIssues = new List<NewIssue>();
            List<string> newCommentInfo = new List<string>();
            GithubClient.Setup(x => x.Issue.Create(It.IsAny<long>(), Capture.In(newIssues))).ReturnsAsync(updateIssue);
            GithubClient.Setup(x => x.Issue.Get(0, 1)).ReturnsAsync(updateIssue);
            GithubClient.Setup(x => x.Issue.Comment.GetAllForIssue(RepoId, IssueNumber)).ReturnsAsync(issueComment);
            GithubClient.Setup(x => x.Issue.Comment.Create(RepoId, IssueNumber, Capture.In(newCommentInfo))).ReturnsAsync(comment);
            DependencyUpdateErrorProcessor errorProcessor =
                ActivatorUtilities.CreateInstance<DependencyUpdateErrorProcessor>(Scope.ServiceProvider,
                    Context);
            await errorProcessor.ProcessDependencyUpdateErrorsAsync();
            Assert.Single(newIssues);
            Assert.Equal("DependencyUpdateError", newIssues[0].Labels[0]);
            Assert.Contains(RepoUrl, newIssues[0].Body);
            Assert.Contains(SubscriptionId, newIssues[0].Body);
            Assert.Contains(SubscriptionId, newIssues[0].Body);
            Assert.Contains(MethodName, newIssues[0].Body);
            Assert.DoesNotContain(SubscriptionId, newCommentInfo[0]);
            Assert.Contains("2/1/2200 12:00:00 AM", newCommentInfo[0]);
            Assert.Contains("ProcessPendingUpdatesAsync", newCommentInfo[0]);
        }

        [Fact]
        public async Task CreateIssueAndUpdateComment()
        {
            const string AnotherMethod = "ProcessPendingUpdatesAsync";
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
                    Method = AnotherMethod,
                    Timestamp = new DateTime(2200, 2, 1),
                    Arguments = "ProcessPendingUpdatesAsync error",
                    Success = false,
                    ErrorMessage = ErrorMessage,
                    Action = "Create a new issue comment",
                };

            RepositoryBranchUpdateHistoryEntry thirdError =
                new RepositoryBranchUpdateHistoryEntry
                {
                    Repository = RepoUrl,
                    Branch = Branch,
                    Method = AnotherMethod,
                    Timestamp = new DateTime(2200, 3, 1),
                    Arguments = "ProcessPendingUpdatesAsync arguments",
                    Success = false,
                    ErrorMessage = ErrorMessage,
                    Action = "Update the comment",
                };
            Context.RepoBranchUpdateInMemory = new List<RepositoryBranchUpdateHistoryEntry>
                {firstError, secondError, thirdError};
            Maestro.Data.Models.Subscription subscription = new Maestro.Data.Models.Subscription
            {
                Id = Guid.Parse(SubscriptionId),
                SourceRepository = "Source Repo",
                TargetRepository = "Target Repo",
            };
            Context.Subscriptions.Add(subscription);
            Context.SaveChanges();
            Repository repository = new Repository();
            GithubClient.Setup(x => x.Repository.Get(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(repository);
            Issue updateIssue = GetIssue();
            Octokit.AuthorAssociation author = new Octokit.AuthorAssociation();
            IssueComment comment = new IssueComment
            (
                1,
                null,
                null,
                null,
                $"[marker]: <> (subscriptionId: '', method: '{AnotherMethod}', errorMessage: '{ErrorMessage}')",
                new DateTime(2200, 02, 02),
                null,
                null,
                null,
                author);
            List<IssueComment> issueComment = new List<IssueComment> { comment };
            List<NewIssue> newIssues = new List<NewIssue>();
            List<string> newCommentInfo = new List<string>();

            GithubClient.Setup(x => x.Issue.Create(It.IsAny<long>(), Capture.In(newIssues))).ReturnsAsync(updateIssue);
            GithubClient.Setup(x => x.Issue.Get(RepoId, IssueNumber)).ReturnsAsync(updateIssue);
            GithubClient.Setup(x => x.Issue.Comment.GetAllForIssue(RepoId, IssueNumber)).ReturnsAsync(issueComment);
            GithubClient.Setup(x => x.Issue.Comment.Create(RepoId, IssueNumber, Capture.In(newCommentInfo))).ReturnsAsync(comment);
            GithubClient.Setup(x => x.Issue.Comment.Update(RepoId, CommentId, Capture.In(newCommentInfo)))
                .ReturnsAsync(comment);
            DependencyUpdateErrorProcessor errorProcessor =
                ActivatorUtilities.CreateInstance<DependencyUpdateErrorProcessor>(Scope.ServiceProvider,
                    Context);
            await errorProcessor.ProcessDependencyUpdateErrorsAsync();
            Assert.Single(newIssues);
            Assert.Equal("DependencyUpdateError", newIssues[0].Labels[0]);
            Assert.Contains(RepoUrl, newIssues[0].Body);
            Assert.Contains(SubscriptionId, newIssues[0].Body);
            Assert.Contains(AnotherMethod, newCommentInfo[0]);
            Assert.DoesNotContain(SubscriptionId, newCommentInfo[0]);
            Assert.Contains("2/1/2200 12:00:00 AM", newCommentInfo[0]);
            Assert.Contains(AnotherMethod, newCommentInfo[1]);
            Assert.Contains("3/1/2200 12:00:00 AM", newCommentInfo[1]);
            Assert.DoesNotContain(SubscriptionId, newCommentInfo[1]);
        }
    }
}
