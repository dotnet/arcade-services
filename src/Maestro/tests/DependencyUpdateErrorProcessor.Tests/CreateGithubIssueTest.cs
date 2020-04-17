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
    public class CreateGithubIssueTest : DependencyUpdateErrorProcessorTests
    {
        [Theory]
        [InlineData("https://github.com/maestro-auth-test/maestro-test2", "38", "ProcessPendingUpdatesAsync", "no arguments" , "2200/1/1" , false)]
        [InlineData("https://github.com/maestro-auth-test/maestro-test2", "38", "UpdateAssetsAsync", "[\"ee8cdcfb-ee51-4bf3-55d3-08d79538f94d\",\"UpdateAssetsAsync\"]",  "2200/1/1", false)]
        public async Task ShouldCreateIssue(string repoUrl, string branch , string method, string arguments , DateTime errorOccurredAt, bool success)
        {
            Mock<RepositoryBranchUpdateHistoryEntry> repositoryBranchUpdate =
                new Mock<RepositoryBranchUpdateHistoryEntry>();
            repositoryBranchUpdate.Object.Repository = repoUrl;
            repositoryBranchUpdate.Object.Branch = branch;
            repositoryBranchUpdate.Object.Method = method;
            repositoryBranchUpdate.Object.Timestamp = errorOccurredAt;
            repositoryBranchUpdate.Object.Arguments = arguments;
            repositoryBranchUpdate.Object.Success = success;
            Mock<Octokit.Repository> repository = new Mock<Repository>();
            GithubClient.Setup(x => x.Repository.Get(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(repository.Object);
            Mock<Maestro.Data.Models.Subscription> subscription = new Mock<Maestro.Data.Models.Subscription>();
            subscription.Object.Id = Guid.Parse("ee8cdcfb-ee51-4bf3-55d3-08d79538f94d");
            Context.Subscriptions.Add(subscription.Object);
            Context.SaveChanges();
            Mock<Octokit.Issue> issue = new Mock<Issue>();
            GithubClient.Setup(x => x.Issue.Create(It.IsAny<long>(),It.IsAny<NewIssue>())).ReturnsAsync(issue.Object);
            Context.RepoBranchUpdateInMemory = new List<RepositoryBranchUpdateHistoryEntry>
                {repositoryBranchUpdate.Object};
            DependencyUpdateErrorProcessor errorProcessor =
                ActivatorUtilities.CreateInstance<DependencyUpdateErrorProcessor>(Scope.ServiceProvider,
                    Context);
            await errorProcessor.ProcessDependencyUpdateErrorsAsync();
            GithubClient.Verify(x => x.Issue.Create(It.IsAny<long>(), It.IsAny<NewIssue>()), Times.Once);
        }


        [Theory]
        [InlineData("https://github.com/maestro-auth-test/maestro-test2", "38", "SynchronizePullRequestAsync", "no arguments", "2200/1/1", false)]
        [InlineData("https://github.com/maestro-auth-test/maestro-test2", "38", "UpdateAssetsAsync", "[\"ee8cdcfb-ee51-4bf3-55d3-08d79538f94d\",\"UpdateAssetsAsync\"]", "2200/1/1", false)]
        public async Task ShouldNotCreateIssue(string repoUrl, string branch, string method, string arguments, DateTime errorOccurredAt, bool success)
        { 
            Mock<RepositoryBranchUpdateHistoryEntry> repositoryBranchUpdate =
                new Mock<RepositoryBranchUpdateHistoryEntry>();
            repositoryBranchUpdate.Object.Repository = repoUrl;
            repositoryBranchUpdate.Object.Branch = branch;
            repositoryBranchUpdate.Object.Method = method;
            repositoryBranchUpdate.Object.Timestamp = errorOccurredAt;
            repositoryBranchUpdate.Object.Arguments = arguments;
            repositoryBranchUpdate.Object.Success = success;
            Mock<Octokit.Repository> repository = new Mock<Repository>();
            GithubClient.Setup(x => x.Repository.Get(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(repository.Object);
            Mock<Octokit.Issue> issue = new Mock<Issue>();
            GithubClient.Setup(x => x.Issue.Create(It.IsAny<long>(), It.IsAny<NewIssue>())).ReturnsAsync(issue.Object);
            Context.RepoBranchUpdateInMemory = new List<RepositoryBranchUpdateHistoryEntry>
                {repositoryBranchUpdate.Object};
            DependencyUpdateErrorProcessor errorProcessor =
                ActivatorUtilities.CreateInstance<DependencyUpdateErrorProcessor>(Scope.ServiceProvider,
                    Context);
            await errorProcessor.ProcessDependencyUpdateErrorsAsync();
            GithubClient.Verify(x => x.Issue.Create(It.IsAny<long>(), It.IsAny<NewIssue>()), Times.Never);
        }

        [Fact]
        public async Task CreateIssue()
        {
            Mock<RepositoryBranchUpdateHistoryEntry> firstError =
                new Mock<RepositoryBranchUpdateHistoryEntry>();
            firstError.Object.Repository = "https://github.com/maestro-auth-test/maestro-test2";
            firstError.Object.Branch = "38";
            firstError.Object.Method = "ProcessPendingUpdatesAsync";
            firstError.Object.Timestamp = new DateTime(2200, 1, 1);
            firstError.Object.Arguments = "[\"ee8cdcfb-ee51-4bf3-55d3-08d79538f94d\",\"UpdateAssetsAsync\",\"build: 14	Unexpected error processing action: Validation Failed\"]";
            firstError.Object.Success = false;
            firstError.Object.ErrorMessage = "build: 14	Unexpected error processing action: Validation Failed";
            firstError.Object.Action = "Creating new issue";

            Mock<RepositoryBranchUpdateHistoryEntry> secondError =
                new Mock<RepositoryBranchUpdateHistoryEntry>();
            secondError.Object.Repository = "https://github.com/maestro-auth-test/maestro-test3";
            secondError.Object.Branch = "38";
            secondError.Object.Method = "ProcessPendingUpdatesAsync";
            secondError.Object.Timestamp = new DateTime(2200, 1, 1);
            secondError.Object.Arguments = "[Arguments]";
            secondError.Object.Success = false;
            secondError.Object.ErrorMessage = "ProcessPendingUpdatesAsync error";
            secondError.Object.Action = "Create another issue";

            Context.RepoBranchUpdateInMemory = new List<RepositoryBranchUpdateHistoryEntry>
                {firstError.Object , secondError.Object};

            Mock<Octokit.Repository> repository = new Mock<Repository>();
            GithubClient.Setup(x => x.Repository.Get(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(repository.Object);
            Mock<Octokit.Issue> issue = new Mock<Issue>();
            List<NewIssue> newIssue = new List<NewIssue>();
            GithubClient.Setup(x => x.Issue.Create(It.IsAny<long>(), Capture.In(newIssue))).ReturnsAsync(issue.Object);
            DependencyUpdateErrorProcessor errorProcessor =
                ActivatorUtilities.CreateInstance<DependencyUpdateErrorProcessor>(Scope.ServiceProvider,
                    Context);
            await errorProcessor.ProcessDependencyUpdateErrorsAsync();
            string firstIssueTitle =
                "[Dependency Update] Errors during dependency updates to : https://github.com/maestro-auth-test/maestro-test2";
            string secondIssueTitle =
                "[Dependency Update] Errors during dependency updates to : https://github.com/maestro-auth-test/maestro-test3";
            string firstIssueBody =
                $@"The following errors have been detected when attempting to update dependencies in 
'https://github.com/maestro-auth-test/maestro-test2'

 [marker]: <> (subscriptionId: '', method: 'ProcessPendingUpdatesAsync', errorMessage: 'build: 14	Unexpected error processing action: Validation Failed')
**Repository :** 'https://github.com/maestro-auth-test/maestro-test2'
**Branch Name :** '38'
**Error Message :**  'build: 14	Unexpected error processing action: Validation Failed'
**Method :**   'ProcessPendingUpdatesAsync'
**Action :**  'Creating new issue'
**Last seen :**  '1/1/2200 12:00:00 AM'
**/FyiHandle :** @epananth";
            string secondIssueBody =
                $@"The following errors have been detected when attempting to update dependencies in 
'https://github.com/maestro-auth-test/maestro-test3'

 [marker]: <> (subscriptionId: '', method: 'ProcessPendingUpdatesAsync', errorMessage: 'ProcessPendingUpdatesAsync error')
**Repository :** 'https://github.com/maestro-auth-test/maestro-test3'
**Branch Name :** '38'
**Error Message :**  'ProcessPendingUpdatesAsync error'
**Method :**   'ProcessPendingUpdatesAsync'
**Action :**  'Create another issue'
**Last seen :**  '1/1/2200 12:00:00 AM'
**/FyiHandle :** @epananth";
            Assert.Equal(2, newIssue.Count);
            Assert.Equal("DependencyUpdateError", newIssue[0].Labels[0]);
            Assert.Equal(firstIssueTitle, newIssue[0].Title);
            Assert.Equal(firstIssueBody,newIssue[0].Body);
            Assert.Equal( "DependencyUpdateError", newIssue[1].Labels[0]);
            Assert.Equal(secondIssueTitle, newIssue[1].Title);
            Assert.Equal(secondIssueBody, newIssue[1].Body);
        }
    }
}
