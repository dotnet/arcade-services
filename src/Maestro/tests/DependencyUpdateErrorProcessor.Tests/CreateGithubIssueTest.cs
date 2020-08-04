// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Maestro.Data;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NUnit.Framework;
using Octokit;

namespace DependencyUpdateErrorProcessor.Tests
{
    [TestFixture, NonParallelizable]
    public class CreateGithubIssueTest : DependencyUpdateErrorProcessorTests
    {
        private const string SubscriptionId = "00000000-0000-0000-0000-000000000001";
        private const string RepoUrl = "https://github.test/test-org-1/test-repo-1";
        private const string BranchOne = "BranchOne";
        private const string BranchTwo = "BranchTwo";
        [TestCase("https://github.test/test-org-1/test-repo-1", "BranchOne", "ProcessPendingUpdatesAsync", "no arguments" , "2200/1/1" , false)]
        [TestCase("https://github.test/test-org-1/test-repo-13", "BranchTwo", "UpdateAssetsAsync", "[\"00000000-0000-0000-0000-000000000001\",\"UpdateAssetsAsync\"]",  "2200/1/1", false)]
        [TestCase("https://github.test/test-org-1/test-repo-1", "BranchOne", "TestMethod", "no arguments", "2200/1/1", false)]
        public async Task ShouldCreateIssue(string repoUrl, string branch , string method, string arguments , DateTime errorOccurredAt, bool success)
        {
            // Not testing SubscriptionUpdateHistoryEntry since this is always "yes" for them.
            RepositoryBranchUpdateHistoryEntry repositoryBranchUpdate =
                new RepositoryBranchUpdateHistoryEntry
                {
                    Repository = repoUrl,
                    Branch = branch,
                    Method = method,
                    Timestamp = errorOccurredAt,
                    Arguments = arguments,
                    Success = success
                };
            Repository repository = new Repository();
            GithubClient.Setup(x => x.Repository.Get(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(repository);
            Mock<Maestro.Data.Models.Subscription> subscription = new Mock<Maestro.Data.Models.Subscription>();
            subscription.Object.Id = Guid.Parse(SubscriptionId);
            Context.Subscriptions.Add(subscription.Object);
            Context.SaveChanges();
            Mock<Issue> issue = new Mock<Issue>();
            //Shared mocks works for Xunit as it creates a separate file for each test, but for Nunit there will be a conflict. 
            //We need to take care of this if we port to Nunit in future.
            GithubClient.Setup(x => x.Issue.Create(It.IsAny<long>(),It.IsAny<NewIssue>())).ReturnsAsync(issue.Object);
            Context.RepoBranchUpdateInMemory = new List<RepositoryBranchUpdateHistoryEntry> {repositoryBranchUpdate};
            DependencyUpdateErrorProcessor errorProcessor =
                ActivatorUtilities.CreateInstance<DependencyUpdateErrorProcessor>(Scope.ServiceProvider,
                    Context);
            await errorProcessor.ProcessDependencyUpdateErrorsAsync();
            GithubClient.Verify(x => x.Issue.Create(It.IsAny<long>(), It.IsAny<NewIssue>()), Times.Once);
        }

        /// <summary>
        /// No issue should be created for the method SynchronizePullRequestAsync
        /// No issue should be created if the subscriptionGuid is invalid for the UpdateAssetsAsync method
        /// No issue should be created if the createdDate for the error is less than current time
        /// No issue is created if the subscription is deleted
        /// </summary>
        /// <param name="repoUrl"></param>
        /// <param name="branch"></param>
        /// <param name="method"></param>
        /// <param name="arguments"></param>
        /// <param name="errorOccurredAt"></param>
        /// <param name="success"></param>
        /// <returns>Does not create any new issue.</returns>
        [TestCase("https://github.test/test-org-1/test-repo-1", "38", "SynchronizePullRequestAsync", "no arguments", "2200/1/1", false)]
        [TestCase("https://github.test/test-org-1/test-repo-13", "38", "UpdateAssetsAsync", "[\"0000000\",\"UpdateAssetsAsync\"]", "2200/1/1", false)]
        [TestCase("https://github.test/test-org-1/test-repo-12", "38", "UpdateAssetsAsync", "[\"00000000-0000-0000-0000-000000000001\",\"UpdateAssetsAsync\"]", "2020/1/1", false)]
        [TestCase("https://github.test/test-org-1/test-repo-1", "38", "UpdateAssetsAsync", "[\"00000000-0000-0000-0000-000000000001\",\"UpdateAssetsAsync\"]", "2200/1/1", false)]
        public async Task ShouldNotCreateIssue(string repoUrl, string branch, string method, string arguments, DateTime errorOccurredAt, bool success)
        {
            // Not testing SubscriptionUpdateHistoryEntry since this is always "no" for them.
            RepositoryBranchUpdateHistoryEntry repositoryBranchUpdate =
                new RepositoryBranchUpdateHistoryEntry
                {
                    Repository = repoUrl,
                    Branch = branch,
                    Method = method,
                    Timestamp = errorOccurredAt,
                    Arguments = arguments,
                    Success = success
                };
            Repository repository = new Repository();
            GithubClient.Setup(x => x.Repository.Get(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(repository);
            Maestro.Data.Models.Subscription subscription = new Maestro.Data.Models.Subscription();
            Context.Subscriptions.Add(subscription); 
            Context.SaveChanges();
            Mock<Issue> issue = new Mock<Issue>();
            GithubClient.Setup(x => x.Issue.Create(It.IsAny<long>(), It.IsAny<NewIssue>())).ReturnsAsync(issue.Object);
            Context.RepoBranchUpdateInMemory = new List<RepositoryBranchUpdateHistoryEntry>
                {repositoryBranchUpdate};
            DependencyUpdateErrorProcessor errorProcessor =
                ActivatorUtilities.CreateInstance<DependencyUpdateErrorProcessor>(Scope.ServiceProvider,
                    Context);
            await errorProcessor.ProcessDependencyUpdateErrorsAsync();
            GithubClient.Verify(x => x.Issue.Create(It.IsAny<long>(), It.IsAny<NewIssue>()), Times.Never);
        }

        [Test]
        public async Task CreateIssue()
        {
            Context.RepoBranchUpdateInMemory = new List<RepositoryBranchUpdateHistoryEntry>
            {
                new RepositoryBranchUpdateHistoryEntry
                {
                    Repository = RepoUrl,
                    Branch = BranchOne,
                    Method = "ProcessPendingUpdatesAsync",
                    Timestamp = new DateTime(2200, 1, 1),
                    Arguments = "[Error Message]",
                    Success = false,
                    ErrorMessage = "Error Message",
                    Action = "Creating new issue"
                },
                new RepositoryBranchUpdateHistoryEntry
                {
                    Repository = RepoUrl,
                    Branch = BranchTwo,
                    Method = "ProcessPendingUpdatesAsync",
                    Timestamp = new DateTime(2200, 1, 1),
                    Arguments = "[Arguments]",
                    Success = false,
                    ErrorMessage = "ProcessPendingUpdatesAsync error",
                    Action = "Create another issue"
                }
            };

            // Other types of issues will cause SubscriptionUpdateHistoryEntry entries to be created.
            Guid subscription1UpdateFailureGuid = Guid.NewGuid();
            Guid subscription2UpdateFailureGuid = Guid.NewGuid();
            Context.SubscriptionUpdateInMemory = new List<SubscriptionUpdateHistoryEntry>
            {
                new SubscriptionUpdateHistoryEntry
                {
                    SubscriptionId = subscription1UpdateFailureGuid,
                    Method = "SomeSubscriptionMethod1",
                    Timestamp = new DateTime(2200, 1, 1),
                    Arguments = "[Error Message]",
                    Success = false,
                    ErrorMessage = "Subscription update error message 1",
                    Action = "Creating new issue"
                },
                new SubscriptionUpdateHistoryEntry
                {
                    SubscriptionId = subscription2UpdateFailureGuid,
                    Method = "SomeSubscriptionMethod2",
                    Timestamp = new DateTime(2200, 1, 1),
                    Arguments = "[Error Message]",
                    Success = false,
                    ErrorMessage = "Subscription update error message 2",
                    Action = "Creating new issue"
                }
            };

            Repository repository = new Repository();
            GithubClient.Setup(x => x.Repository.Get(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(repository);
            Mock<Issue> issue = new Mock<Issue>();
            List<NewIssue> newIssue = new List<NewIssue>();
            GithubClient.Setup(x => x.Issue.Create(It.IsAny<long>(), Capture.In(newIssue))).ReturnsAsync(issue.Object);
            DependencyUpdateErrorProcessor errorProcessor =
                ActivatorUtilities.CreateInstance<DependencyUpdateErrorProcessor>(Scope.ServiceProvider,
                    Context);
            await errorProcessor.ProcessDependencyUpdateErrorsAsync();
            newIssue.Should().HaveCount(4);

            // Repo Branch updates
            newIssue[0].Labels[0].Should().Be("DependencyUpdateError");
            newIssue[0].Title.Should().Contain(RepoUrl);
            newIssue[0].Body.Should().Contain(BranchOne);
            newIssue[0].Body.Should().Contain("ProcessPendingUpdatesAsync");
            newIssue[0].Body.Should().Contain("1/1/2200 12:00:00 AM");
            newIssue[0].Body.Should().Contain(RepoUrl);

            newIssue[1].Labels[0].Should().Be("DependencyUpdateError");
            newIssue[1].Title.Should().Contain(RepoUrl);
            newIssue[1].Body.Should().Contain(BranchTwo);
            newIssue[1].Body.Should().Contain("ProcessPendingUpdatesAsync error");
            newIssue[1].Body.Should().Contain("1/1/2200 12:00:00 AM");
            newIssue[1].Body.Should().Contain(RepoUrl);

            // Subscription Updates
            newIssue[2].Labels[0].Should().Be("DependencyUpdateError");
            newIssue[2].Title.Should().Contain(subscription1UpdateFailureGuid.ToString());
            newIssue[2].Title.Should().Contain("Subscription Update");
            newIssue[2].Body.Should().Contain(subscription1UpdateFailureGuid.ToString());
            newIssue[2].Body.Should().Contain("Subscription update error message 1");
            newIssue[2].Body.Should().Contain("1/1/2200 12:00:00 AM");

            newIssue[3].Labels[0].Should().Be("DependencyUpdateError");
            newIssue[3].Title.Should().Contain(subscription2UpdateFailureGuid.ToString());
            newIssue[3].Title.Should().Contain("Subscription Update");
            newIssue[3].Body.Should().Contain(subscription2UpdateFailureGuid.ToString());
            newIssue[3].Body.Should().Contain("Subscription update error message 2");
            newIssue[3].Body.Should().Contain("1/1/2200 12:00:00 AM");
        }
    }
}
