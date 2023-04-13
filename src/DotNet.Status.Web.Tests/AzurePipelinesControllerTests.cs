using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DotNet.Status.Web.Options;
using DotNet.Status.Web.Controllers;
using Microsoft.DotNet.GitHub.Authentication;
using Microsoft.DotNet.Internal.AzureDevOps;
using Microsoft.DotNet.Internal.Testing.Utility;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using FluentAssertions;
using Microsoft.DotNet.Internal.DependencyInjection;
using Microsoft.DotNet.Internal.Testing.DependencyInjection.Abstractions;
using Moq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Octokit;
using Microsoft.DotNet.Services.Utility;

namespace DotNet.Status.Web.Tests;

[TestFixture]
public partial class AzurePipelinesControllerTests
{
    [Test]
    public async Task BuildCompleteBuildHasNoTags()
    {
        var buildEvent = new AzurePipelinesController.AzureDevOpsEvent<AzurePipelinesController.AzureDevOpsMinimalBuildResource>
        {
            Resource = new AzurePipelinesController.AzureDevOpsMinimalBuildResource
            {
                Id = 123456,
                Url = "test-build-url"
            },
            ResourceContainers = new AzurePipelinesController.AzureDevOpsResourceContainers
            {
                Collection = new AzurePipelinesController.HasId
                {
                    Id = "test-collection-id"
                },
                Account = new AzurePipelinesController.HasId
                {
                    Id = "test-account-id"
                },
                Project = new AzurePipelinesController.HasId
                {
                    Id = "test-project-id"
                }
            }
        };

        var build = new JObject
        {
            ["_links"] = new JObject
            {
                ["web"] = new JObject
                {
                    ["href"] = "href"
                }
            },
            ["buildNumber"] = "123456",
            ["definition"] = new JObject
            {
                ["name"] = "path",
                ["path"] = "\\test\\definition"
            },
            ["finishTime"] = "05/01/2008 6:00:00",
            ["id"] = "123",
            ["project"] = new JObject
            {
                ["name"] = "test-project-name"
            },
            ["reason"] = "batchedCI",
            ["requestedFor"] = new JObject
            {
                ["displayName"] = "requested-for"
            },
            ["result"] = "failed",
            ["sourceBranch"] = "refs/heads/sourceBranch",
            ["startTime"] = "05/01/2008 5:00:00",
        };

        var expectedOwners = new List<string>
        {
            "dotnet"
        };

        var expectedNames = new List<string>
        {
            "repo"
        };
        var expectedCommentOwners = new List<string>();
        var expectedCommentNames = new List<string>();

        await using TestData testData = await TestData.Default.WithBuildData(build).WithExpectMatchingTitle(false).BuildAsync();
        var response = await testData.Controller.BuildComplete(buildEvent);
        VerifyGitHubCalls(testData, expectedOwners, expectedNames, expectedCommentOwners, expectedCommentNames);
    }

    [Test]
    public async Task BuildCompleteBuildHasMatchingTags()
    {
        var buildEvent = new AzurePipelinesController.AzureDevOpsEvent<AzurePipelinesController.AzureDevOpsMinimalBuildResource>
        {
            Resource = new AzurePipelinesController.AzureDevOpsMinimalBuildResource
            {
                Id = 123456,
                Url = "test-build-url"
            },
            ResourceContainers = new AzurePipelinesController.AzureDevOpsResourceContainers
            {
                Collection = new AzurePipelinesController.HasId
                {
                    Id = "test-collection-id"
                },
                Account = new AzurePipelinesController.HasId
                {
                    Id = "test-account-id"
                },
                Project = new AzurePipelinesController.HasId
                {
                    Id = "test-project-id"
                }
            }
        };

        var build = new JObject
        {
            ["_links"] = new JObject
            {
                ["web"] = new JObject
                {
                    ["href"] = "href"
                }
            },
            ["buildNumber"] = "123456",
            ["definition"] = new JObject
            {
                ["name"] = "path2",
                ["path"] = "\\test\\definition"
            },
            ["finishTime"] = "05/01/2008 6:00:00",
            ["id"] = "123",
            ["project"] = new JObject
            {
                ["name"] = "test-project-name"
            },
            ["reason"] = "batchedCI",
            ["requestedFor"] = new JObject
            {
                ["displayName"] = "requested-for"
            },
            ["result"] = "failed",
            ["sourceBranch"] = "refs/heads/sourceBranch",
            ["startTime"] = "05/01/2008 5:00:00",
            ["tags"] = new JArray
            {
                "tag1"
            }
        };

        var expectedOwners = new List<string>
        {
            "dotnet"
        };

        var expectedNames = new List<string>
        {
            "repo"
        };
        var expectedCommentOwners = new List<string>();
        var expectedCommentNames = new List<string>();

        using TestData testData = await TestData.Default.WithBuildData(build).WithExpectMatchingTitle(false).BuildAsync();
        var response = await testData.Controller.BuildComplete(buildEvent);
        VerifyGitHubCalls(testData, expectedOwners, expectedNames, expectedCommentOwners, expectedCommentNames);
    }

    [Test]
    public async Task BuildCompleteBuildHasNoMatchingTags()
    {
        var buildEvent = new AzurePipelinesController.AzureDevOpsEvent<AzurePipelinesController.AzureDevOpsMinimalBuildResource>
        {
            Resource = new AzurePipelinesController.AzureDevOpsMinimalBuildResource
            {
                Id = 123456,
                Url = "test-build-url"
            },
            ResourceContainers = new AzurePipelinesController.AzureDevOpsResourceContainers
            {
                Collection = new AzurePipelinesController.HasId
                {
                    Id = "test-collection-id"
                },
                Account = new AzurePipelinesController.HasId
                {
                    Id = "test-account-id"
                },
                Project = new AzurePipelinesController.HasId
                {
                    Id = "test-project-id"
                }
            }
        };

        var build = new JObject
        {
            ["_links"] = new JObject
            {
                ["web"] = new JObject
                {
                    ["href"] = "href"
                }
            },
            ["buildNumber"] = "123456",
            ["definition"] = new JObject
            {
                ["name"] = "path2",
                ["path"] = "\\test\\definition"
            },
            ["finishTime"] = "05/01/2008 6:00:00",
            ["id"] = "123",
            ["project"] = new JObject
            {
                ["name"] = "test-project-name"
            },
            ["reason"] = "batchedCI",
            ["requestedFor"] = new JObject
            {
                ["displayName"] = "requested-for"
            },
            ["result"] = "failed",
            ["sourceBranch"] = "refs/heads/sourceBranch",
            ["startTime"] = "05/01/2008 5:00:00",
            ["tags"] = new JArray
            {
                "non-matching-tag"
            }
        };

        var expectedOwners = new List<string>();
        var expectedNames = new List<string>();
        var expectedCommentOwners = new List<string>();
        var expectedCommentNames = new List<string>();

        using TestData testData = await TestData.Default.WithBuildData(build).WithExpectMatchingTitle(false).BuildAsync();
        var response = await testData.Controller.BuildComplete(buildEvent);
        VerifyGitHubCalls(testData, expectedOwners, expectedNames, expectedCommentOwners, expectedCommentNames);
    }

    [Test]
    public async Task BuildCompleteUpdateExistingIssueExists()
    {
        var buildEvent = new AzurePipelinesController.AzureDevOpsEvent<AzurePipelinesController.AzureDevOpsMinimalBuildResource>
        {
            Resource = new AzurePipelinesController.AzureDevOpsMinimalBuildResource
            {
                Id = 123456,
                Url = "test-build-url"
            },
            ResourceContainers = new AzurePipelinesController.AzureDevOpsResourceContainers
            {
                Collection = new AzurePipelinesController.HasId
                {
                    Id = "test-collection-id"
                },
                Account = new AzurePipelinesController.HasId
                {
                    Id = "test-account-id"
                },
                Project = new AzurePipelinesController.HasId
                {
                    Id = "test-project-id"
                }
            }
        };

        var build = new JObject
        {
            ["_links"] = new JObject
            {
                ["web"] = new JObject
                {
                    ["href"] = "href"
                }
            },
            ["buildNumber"] = "123456",
            ["definition"] = new JObject
            {
                ["name"] = "path3",
                ["path"] = "\\test\\definition"
            },
            ["finishTime"] = "05/01/2008 6:00:00",
            ["id"] = "123",
            ["project"] = new JObject
            {
                ["name"] = "test-project-name"
            },
            ["reason"] = "batchedCI",
            ["requestedFor"] = new JObject
            {
                ["displayName"] = "requested-for"
            },
            ["result"] = "failed",
            ["sourceBranch"] = "refs/heads/sourceBranch",
            ["startTime"] = "05/01/2008 5:00:00",
        };

        var expectedIssueOwners = new List<string>();
        var expectedIssueNames = new List<string>();

        var expectedCommentOwners = new List<string>
        {
            "dotnet"
        };
        var expectedCommentNames = new List<string>
        {
            "repo"
        };

        await using TestData testData = await TestData.Default.WithBuildData(build).WithExpectMatchingTitle(true).BuildAsync();
        var response = await testData.Controller.BuildComplete(buildEvent);
        VerifyGitHubCalls(testData, expectedIssueOwners, expectedIssueNames, expectedCommentOwners, expectedCommentNames);
    }

    [Test]
    public async Task BuildCompleteUpdateExistingIssueDoesNotExist()
    {
        var buildEvent = new AzurePipelinesController.AzureDevOpsEvent<AzurePipelinesController.AzureDevOpsMinimalBuildResource>
        {
            Resource = new AzurePipelinesController.AzureDevOpsMinimalBuildResource
            {
                Id = 123456,
                Url = "test-build-url"
            },
            ResourceContainers = new AzurePipelinesController.AzureDevOpsResourceContainers
            {
                Collection = new AzurePipelinesController.HasId
                {
                    Id = "test-collection-id"
                },
                Account = new AzurePipelinesController.HasId
                {
                    Id = "test-account-id"
                },
                Project = new AzurePipelinesController.HasId
                {
                    Id = "test-project-id"
                }
            }
        };

        var build = new JObject
        {
            ["_links"] = new JObject
            {
                ["web"] = new JObject
                {
                    ["href"] = "href"
                }
            },
            ["buildNumber"] = "123456",
            ["definition"] = new JObject
            {
                ["name"] = "path3",
                ["path"] = "\\test\\definition"
            },
            ["finishTime"] = "05/01/2008 6:00:00",
            ["id"] = "123",
            ["project"] = new JObject
            {
                ["name"] = "test-project-name"
            },
            ["reason"] = "batchedCI",
            ["requestedFor"] = new JObject
            {
                ["displayName"] = "requested-for"
            },
            ["result"] = "failed",
            ["sourceBranch"] = "refs/heads/sourceBranch",
            ["startTime"] = "05/01/2008 5:00:00",
        };

        var expectedIssueOwners = new List<string>
        {
            "dotnet"
        };
        var expectedIssueNames = new List<string>
        {
            "repo"
        };

        var expectedCommentOwners = new List<string>();
        var expectedCommentNames = new List<string>();

        await using TestData testData = await TestData.Default.WithBuildData(build).WithExpectMatchingTitle(false).BuildAsync();
        var response = await testData.Controller.BuildComplete(buildEvent);
        VerifyGitHubCalls(testData, expectedIssueOwners, expectedIssueNames, expectedCommentOwners, expectedCommentNames);
    }

    [TestDependencyInjectionSetup]
    public static class TestDataConfiguration
    {
        public static void Default(IServiceCollection collection)
        {
            collection.AddOptions();
            collection.AddLogging(l => { l.AddProvider(new NUnitLogger()); });

            collection.Configure<BuildMonitorOptions>(
                options =>
                {
                    options.Monitor = new BuildMonitorOptions.AzurePipelinesOptions
                    {
                        Organization = "dnceng",
                        Builds = new[]
                        {
                            new BuildMonitorOptions.AzurePipelinesOptions.BuildDescription
                            {
                                Project = "test-project-name",
                                DefinitionPath = "\\test\\definition\\path",
                                Branches = new[] {"sourceBranch"},
                                Assignee = "assignee",
                                IssuesId = "first-issues"
                            },
                            new BuildMonitorOptions.AzurePipelinesOptions.BuildDescription
                            {
                                Project = "test-project-name",
                                DefinitionPath = "\\test\\definition\\path2",
                                Branches = new string[] {"sourceBranch"},
                                Assignee = "assignee",
                                IssuesId = "first-issues",
                                Tags = new[] {"tag1"}
                            },
                            new BuildMonitorOptions.AzurePipelinesOptions.BuildDescription
                            {
                                Project = "test-project-name",
                                DefinitionPath = "\\test\\definition\\path3",
                                Branches = new string[] { "sourceBranch" },
                                Assignee = "assignee",
                                IssuesId = "second-issues"
                            }
                        }
                    };
                    options.Issues = new[]
                    {
                        new BuildMonitorOptions.IssuesOptions
                        {
                            Id = "first-issues",
                            Owner = "dotnet",
                            Name = "repo",
                            Labels = new[] {"label"}
                        },
                        new BuildMonitorOptions.IssuesOptions
                        {
                            Id = "second-issues",
                            Owner = "dotnet",
                            Name = "repo",
                            Labels = new string[] { "label" },
                            UpdateExisting = true
                        }
                    };
                }
            );
        }

        public static Func<IServiceProvider, AzurePipelinesController> Controller(IServiceCollection collection)
        {
            collection.AddScoped<AzurePipelinesController>();
            return s => s.GetRequiredService<AzurePipelinesController>();
        }

        public static
            Func<IServiceProvider, (List<string> IssueNames, List<string> IssueOwners, List<string> CommentNames, List<string> CommentOwners)>
            GitHubCalls(IServiceCollection collection, JObject buildData, bool expectMatchingTitle)
        {
            var commentOwners = new List<string>();
            var commentNames = new List<string>();
            var mockGithubComments = new Mock<IIssueCommentsClient>();
            mockGithubComments.Setup(
                    m => 
                        m.Create(Capture.In(commentOwners), 
                            Capture.In(commentNames), 
                            It.IsAny<int>(), 
                            It.IsAny<string>()))
                .Returns(Task.FromResult(new Octokit.IssueComment()));

            string title =
                expectMatchingTitle ?
                    $"Build failed: {buildData["definition"]["name"].ToString()}/{buildData["sourceBranch"].ToString().Substring("refs/heads/".Length)} " :
                    "";

            Octokit.Issue mockIssue = new Issue(
                "url", "html", "comments", "events", 123456, ItemState.Open, title,
                "body", null, null, null, null, null, null, 1, null, null, DateTimeOffset.MinValue,
                null, 123456, "nodeid", false, null, null);
                    
            var issueOwners = new List<string>();
            var issueNames = new List<string>();
            var mockGithubIssues = new Mock<IIssuesClient>();
            mockGithubIssues
                .SetupGet(m => m.Comment)
                .Returns(mockGithubComments.Object);
            mockGithubIssues
                .Setup(m => m.Create(Capture.In(issueOwners), Capture.In(issueNames), It.IsAny<Octokit.NewIssue>()))
                .Returns(Task.FromResult(new Octokit.Issue()));
            mockGithubIssues
                .Setup(m => m.GetAllForRepository(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<RepositoryIssueRequest>()))
                .Returns(Task.FromResult((IReadOnlyList<Issue>)(new List<Issue> {mockIssue})));

            Octokit.GitHubApp mockGithubApp = new GitHubApp(12345, "app", null, "desc", "url", "url", DateTimeOffset.MinValue, DateTimeOffset.MinValue);

            var mockGithubAppsClient = new Mock<IGitHubAppsClient>();
            mockGithubAppsClient.Setup(m => m.GetCurrent()).Returns(Task.FromResult(mockGithubApp));

            var mockGithubClient = new Mock<IGitHubClient>();
            mockGithubClient.SetupGet(m => m.Issue).Returns(mockGithubIssues.Object);
            mockGithubClient.SetupGet(m => m.GitHubApps).Returns(mockGithubAppsClient.Object);

            var mockGithubClientFactory = new Mock<IGitHubApplicationClientFactory>();
            mockGithubClientFactory.Setup(m => m.CreateGitHubClientAsync(It.IsAny<string>(), It.IsAny<string>()))
                .Returns(Task.FromResult(mockGithubClient.Object));
            mockGithubClientFactory.Setup(m => m.CreateGitHubAppClient()).Returns(mockGithubClient.Object);

            var mockAzureDevOpsClient = new Mock<IAzureDevOpsClient>();
            mockAzureDevOpsClient.Setup(m => m.GetProjectNameAsync("test-project-id"))
                .Returns(Task.FromResult("test-project-name"));
            if (buildData != null)
            {
                var build = JsonConvert.DeserializeObject<Build>(buildData.ToString());
                mockAzureDevOpsClient
                    .Setup(
                        m => m.GetBuildAsync(It.IsAny<string>(), It.IsAny<long>(), It.IsAny<CancellationToken>())
                    )
                    .Returns(Task.FromResult(build));
            }
            mockAzureDevOpsClient
                .Setup(
                    m => m.GetBuildChangesAsync(It.IsAny<string>(), It.IsAny<long>(), It.IsAny<CancellationToken>())
                )
                .Returns(Task.FromResult(((BuildChange[] changes, int? truncatedChangeCount)?)(new BuildChange[0], 0)));
            mockAzureDevOpsClient
                .Setup(m => m.GetTimelineAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .Returns(Task.FromResult(new Timeline()));
                
            var mockHttpClientFactory = new Mock<System.Net.Http.IHttpClientFactory>();

            var exponentialRetryOptions = new ExponentialRetryOptions();


            collection.AddSingleton(mockGithubClientFactory.Object);
            collection.AddSingleton<IClientFactory<IAzureDevOpsClient>>(new SingleClientFactory<IAzureDevOpsClient>(mockAzureDevOpsClient.Object));
            collection.AddSingleton(mockHttpClientFactory.Object);
            collection.AddSingleton(ExponentialRetry.Default);

            return _ => (issueNames, issueOwners, commentNames, commentOwners);
        }
    }

    private void VerifyGitHubCalls(
        TestData testData, 
        List<string> expectedOwners, 
        List<string> expectedNames,
        List<string> expectedCommentOwners,
        List<string> expectedCommentNames)
    {
        testData.GitHubCalls.IssueOwners.Should().BeEquivalentTo(expectedOwners);
        testData.GitHubCalls.IssueNames.Should().BeEquivalentTo(expectedNames);
        testData.GitHubCalls.CommentOwners.Should().BeEquivalentTo(expectedCommentOwners);
        testData.GitHubCalls.CommentNames.Should().BeEquivalentTo(expectedCommentNames);
    }
}
