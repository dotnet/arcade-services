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
        [Fact]
        public async Task CreateIssueAndUpdateIssueBody()
        {
            Mock<RepositoryBranchUpdateHistoryEntry> firstError =
                new Mock<RepositoryBranchUpdateHistoryEntry>();
            firstError.Object.Repository = "https://github.com/maestro-auth-test/maestro-test2";
            firstError.Object.Branch = "38";
            firstError.Object.Method = "UpdateAssetsAsync";
            firstError.Object.Timestamp = new DateTime(2200, 1, 1);
            firstError.Object.Arguments = "[\"ee8cdcfb-ee51-4bf3-55d3-08d79538f94d\",\"UpdateAssetsAsync\",\"build: 14	Unexpected error processing action: Validation Failed\"]";
            firstError.Object.Success = false;
            firstError.Object.ErrorMessage = "build: 14	Unexpected error processing action: Validation Failed";
            firstError.Object.Action = "Creating new issue";

            Mock<RepositoryBranchUpdateHistoryEntry> secondError =
                new Mock<RepositoryBranchUpdateHistoryEntry>();
            secondError.Object.Repository = "https://github.com/maestro-auth-test/maestro-test2";
            secondError.Object.Branch = "38";
            secondError.Object.Method = "UpdateAssetsAsync";
            secondError.Object.Timestamp = new DateTime(2200, 2, 1);
            secondError.Object.Arguments = "[\"ee8cdcfb-ee51-4bf3-55d3-08d79538f94d\",\"UpdateAssetsAsync\",\"build: 14	Unexpected error processing action: Validation Failed\"]";
            secondError.Object.Success = false;
            secondError.Object.ErrorMessage = "build: 14	Unexpected error processing action: Validation Failed";
            secondError.Object.Action = "Updating existing issue";

            Context.RepoBranchUpdateInMemory = new List<RepositoryBranchUpdateHistoryEntry>
                {firstError.Object , secondError.Object};
            Mock<Maestro.Data.Models.Subscription> subscription = new Mock<Maestro.Data.Models.Subscription>();
            subscription.Object.Id = Guid.Parse("ee8cdcfb-ee51-4bf3-55d3-08d79538f94d");
            subscription.Object.SourceRepository = "Source Repo";
            subscription.Object.TargetRepository = "Target Repo";
            Context.Subscriptions.Add(subscription.Object);
            Context.SaveChanges();

            Mock<Octokit.Repository> repository = new Mock<Repository>();
            GithubClient.Setup(x => x.Repository.Get(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(repository.Object);
            Issue issueCreated = GetIssue();
            List<NewIssue> newIssues = new List<NewIssue>();
            GithubClient.Setup(x => x.Issue.Create(It.IsAny<long>(), Capture.In(newIssues))).ReturnsAsync(issueCreated);
            GithubClient.Setup(x => x.Issue.Get(0, 1)).ReturnsAsync(issueCreated);
            List<IssueUpdate> issueUpdate = new List<IssueUpdate>();
            GithubClient.Setup(x => x.Issue.Update(0, 1, Capture.In(issueUpdate))).ReturnsAsync(issueCreated);
            DependencyUpdateErrorProcessor errorProcessor =
                ActivatorUtilities.CreateInstance<DependencyUpdateErrorProcessor>(Scope.ServiceProvider,
                    Context);
            await errorProcessor.ProcessDependencyUpdateErrorsAsync();
            string issueBody = $@"The following errors have been detected when attempting to update dependencies in 
'https://github.com/maestro-auth-test/maestro-test2'

 [marker]: <> (subscriptionId: 'ee8cdcfb-ee51-4bf3-55d3-08d79538f94d', method: 'UpdateAssetsAsync', errorMessage: 'build: 14	Unexpected error processing action: Validation Failed')
**SubscriptionId:** 'ee8cdcfb-ee51-4bf3-55d3-08d79538f94d'
**Source Repository :**  'Source Repo'
**Target Repository :**  'Target Repo'
**Branch Name :**  '38'
**Error Message :**  'build: 14	Unexpected error processing action: Validation Failed'
**Method :** 'UpdateAssetsAsync'
**Action :** 'Creating new issue'
**Last seen :** '1/1/2200 12:00:00 AM'
**/FyiHandle :** @epananth";
            string updatedIssueBody =
                $@"The following errors have been detected when attempting to update dependencies in
'https://github.com/maestro-auth-test/maestro-test2'

  [marker]: <> (subscriptionId: 'ee8cdcfb-ee51-4bf3-55d3-08d79538f94d', method: 'UpdateAssetsAsync', errorMessage: 'build: 14	Unexpected error processing action: Validation Failed')
**SubscriptionId:** 'ee8cdcfb-ee51-4bf3-55d3-08d79538f94d'
**Source Repository :**  'Source Repo'
**Target Repository :**  'Target Repo'
**Branch Name :**  '38'
**Error Message :**  'build: 14	Unexpected error processing action: Validation Failed'
**Method :** 'UpdateAssetsAsync'
**Action :** 'Updating existing issue'
**Last seen :** '2/1/2200 12:00:00 AM'
**/FyiHandle :** @epananth";
            GithubClient.Verify(x => x.Issue.Update(0, 1, It.IsAny<IssueUpdate>()), Times.Once);
            Assert.Single(newIssues);
            Assert.Equal("[Dependency Update] Errors during dependency updates to : https://github.com/maestro-auth-test/maestro-test2", newIssues[0].Title);
            Assert.Equal("DependencyUpdateError", newIssues[0].Labels[0]);
            Assert.Equal(issueBody, newIssues[0].Body);
            Assert.Equal(updatedIssueBody, issueUpdate[0].Body);
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
                "[marker]: <> (subscriptionId: 'ee8cdcfb-ee51-4bf3-55d3-08d79538f94d', method: 'UpdateAssetsAsync', errorMessage: 'build: 14	Unexpected error processing action: Validation Failed')",
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
