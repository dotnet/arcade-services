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
using Microsoft.DotNet.Internal.Testing.DependencyInjection.Abstractions;
using Moq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Octokit;

namespace DotNet.Status.Web.Tests
{

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

            await using TestData testData = await TestData.Default.WithBuildData(build).BuildAsync();
            var response = await testData.Controller.BuildComplete(buildEvent);
            VerifyGitHubCalls(testData, expectedOwners, expectedNames);
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

            using TestData testData = await TestData.Default.WithBuildData(build).BuildAsync();
            var response = await testData.Controller.BuildComplete(buildEvent);
            VerifyGitHubCalls(testData, expectedOwners, expectedNames);
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

            using TestData testData = await TestData.Default.WithBuildData(build).BuildAsync();
            var response = await testData.Controller.BuildComplete(buildEvent);
            VerifyGitHubCalls(testData, expectedOwners, expectedNames);
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
                            BaseUrl = "https://example.test",
                            Organization = "dnceng",
                            MaxParallelRequests = 10,
                            AccessToken = "fake",
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
                Func<IServiceProvider, (List<string> Names, List<string> Owners)>
                GitHubCalls(IServiceCollection collection, JObject buildData)
            {
                var owners = new List<string>();
                var names = new List<string>();
                var mockGithubIssues = new Mock<IIssuesClient>();
                mockGithubIssues
                    .Setup(m => m.Create(Capture.In(owners), Capture.In(names), It.IsAny<Octokit.NewIssue>()))
                    .Returns(Task.FromResult(new Octokit.Issue()));

                var mockGithubClient = new Mock<IGitHubClient>();
                mockGithubClient.SetupGet(m => m.Issue).Returns(mockGithubIssues.Object);

                var mockGithubClientFactory = new Mock<IGitHubApplicationClientFactory>();
                mockGithubClientFactory.Setup(m => m.CreateGitHubClientAsync(It.IsAny<string>(), It.IsAny<string>()))
                    .Returns(Task.FromResult(mockGithubClient.Object));

                var project = new[]
                {
                    new AzureDevOpsProject("test-project-id", "test-project-name", "", "", "", 0, "")
                };

                var mockAzureDevOpsClient = new Mock<IAzureDevOpsClient>();
                mockAzureDevOpsClient.Setup(m => m.ListProjectsAsync(It.IsAny<CancellationToken>()))
                    .Returns(Task.FromResult(project));
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
                    .Returns(Task.FromResult((new BuildChange[0], 0)));
                mockAzureDevOpsClient
                    .Setup(m => m.GetTimelineAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                    .Returns(Task.FromResult(new Timeline()));

                var mockAzureClientFactory = new Mock<IAzureDevOpsClientFactory>();
                mockAzureClientFactory
                    .Setup(
                        m => m.CreateAzureDevOpsClient(
                            It.IsAny<string>(),
                            It.IsAny<string>(),
                            It.IsAny<int>(),
                            It.IsAny<string>()
                        )
                    )
                    .Returns(mockAzureDevOpsClient.Object);

                collection.AddSingleton(mockGithubClientFactory.Object);
                collection.AddSingleton(mockAzureClientFactory.Object);

                return _ => (names, owners);
            }
        }

        private void VerifyGitHubCalls(TestData testData, List<string> expectedOwners, List<string> expectedNames)
        {
            testData.GitHubCalls.Owners.Should().BeEquivalentTo(expectedOwners);
            testData.GitHubCalls.Names.Should().BeEquivalentTo(expectedNames);
        }
    }
}
