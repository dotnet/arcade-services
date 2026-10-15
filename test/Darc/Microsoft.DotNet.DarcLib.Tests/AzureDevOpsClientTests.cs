// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Net.Http;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.Models;
using Microsoft.DotNet.Internal.AzureDevOps.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Microsoft.DotNet.DarcLib.Tests;

[TestFixture]
public class AzureDevOpsClientTests
{
    private const string PullRequestUrl = "https://dev.azure.com/dnceng/internal/_apis/git/repositories/test/pullRequests/123";
    private const string PullRequestPath = "_apis/git/repositories/test/pullRequests/123";
    private const string ProjectId = "project-id";

    [TestCase(null, CheckState.None)]
    [TestCase("notSet", CheckState.None)]
    [TestCase("pending", CheckState.Pending)]
    [TestCase("succeeded", CheckState.Success)]
    [TestCase("notApplicable", CheckState.None)]
    [TestCase("failed", CheckState.Failure)]
    [TestCase("partiallySucceeded", CheckState.None)]
    [TestCase("error", CheckState.Error)]
    [TestCase("unknown", CheckState.None)]
    public async Task GetPullRequestChecksAsync_MapsExternalStatusState(string state, CheckState expectedState)
    {
        // Arrange
        JObject status = CreateStatus(state);
        var client = CreateClient(new JArray(status));

        // Act
        var checks = await client.Object.GetPullRequestChecksAsync(PullRequestUrl);

        // Assert
        checks.Should().ContainSingle().Which.Status.Should().Be(expectedState);
    }

    [TestCase(false, null, "ci")]
    [TestCase(true, null, "ci")]
    [TestCase(true, "", "ci")]
    [TestCase(true, "build", "build.ci")]
    public async Task GetPullRequestChecksAsync_NormalizesOptionalGenre(bool includeGenre, string genre, string expectedName)
    {
        // Arrange
        JObject status = CreateStatus("succeeded");
        if (includeGenre)
        {
            status["context"]["genre"] = genre;
        }
        var client = CreateClient(new JArray(status));

        // Act
        var checks = await client.Object.GetPullRequestChecksAsync(PullRequestUrl);

        // Assert
        var check = checks.Should().ContainSingle().Which;
        check.Name.Should().Be(expectedName);
        check.Url.Should().BeEmpty();
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task GetPullRequestChecksAsync_RejectsMissingOrEmptyContextName(bool emptyName)
    {
        // Arrange
        JObject status = CreateStatus("succeeded");
        status["context"] = emptyName ? new JObject { ["name"] = "" } : null;
        var client = CreateClient(new JArray(status));

        // Act
        Func<Task> action = () => client.Object.GetPullRequestChecksAsync(PullRequestUrl);

        // Assert
        await action.Should().ThrowAsync<DarcException>().WithMessage("*has no context name*");
    }

    [Test]
    public async Task GetPullRequestChecksAsync_ReadsScenarioFailureAlongsidePolicyAndUnsetStatuses()
    {
        // Arrange
        JObject scenarioStatus = CreateStatus("error");
        scenarioStatus["context"] = new JObject
        {
            ["genre"] = "scenario-test",
            ["name"] = "batching-blocker",
        };
        var client = CreateClient(new JArray(CreateStatus(null), scenarioStatus));
        SetupResponse(client, $"_apis/policy/evaluations?artifactId=vstfs:///CodeReview/CodeReviewId/{ProjectId}/123",
            JObject.Parse("""
                {"value":[{"status":"running","configuration":{"isEnabled":true,"type":{"displayName":"Build"},"url":"https://example.com/build"}}]}
                """), "5.1-preview.1");

        // Act
        var checks = await client.Object.GetPullRequestChecksAsync(PullRequestUrl);

        // Assert
        checks.Should().HaveCount(3);
        checks.Should().Contain(check => check.Name == "Build" && check.Status == CheckState.Pending);
        checks.Should().Contain(check => check.Name == "scenario-test.batching-blocker" && check.Status == CheckState.Error);
    }

    private static JObject CreateStatus(string state)
    {
        var status = new JObject
        {
            ["context"] = new JObject { ["name"] = "ci" },
        };
        if (state != null)
        {
            status["state"] = state;
        }
        return status;
    }

    private static Mock<AzureDevOpsClient> CreateClient(JArray statuses)
    {
        var client = new Mock<AzureDevOpsClient>(
            Mock.Of<IAzureDevOpsTokenProvider>(),
            Mock.Of<IProcessManager>(),
            NullLogger.Instance)
        {
            CallBase = true,
        };
        client.Setup(c => c.ExecuteAzureDevOpsAPIRequestAsync(
            It.IsAny<HttpMethod>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<ILogger>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<string>(), It.IsAny<int>()))
            .Throws(new InvalidOperationException("Unexpected Azure DevOps request in test."));
        SetupResponse(client, "_apis/projects/internal", new JObject { ["id"] = ProjectId }, "5.0");
        SetupResponse(client, $"_apis/policy/evaluations?artifactId=vstfs:///CodeReview/CodeReviewId/{ProjectId}/123",
            new JObject { ["value"] = new JArray() }, "5.1-preview.1");
        SetupResponse(client, $"{PullRequestPath}/statuses", new JObject { ["value"] = statuses }, "7.1");
        return client;
    }

    private static void SetupResponse(Mock<AzureDevOpsClient> client, string path, JObject response, string version)
        => client.Setup(c => c.ExecuteAzureDevOpsAPIRequestAsync(
            HttpMethod.Get, "dnceng", It.IsAny<string>(), path, It.IsAny<ILogger>(), null, version, true, null, 15))
            .ReturnsAsync(response);
}
