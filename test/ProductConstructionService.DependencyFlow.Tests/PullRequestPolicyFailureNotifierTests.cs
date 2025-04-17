// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Threading;
using System.Threading.Tasks;
using Shouldly;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.ProductConstructionService.Client.Models;
using Microsoft.DotNet.ProductConstructionService.DependencyFlow;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;

namespace ProductConstructionService.DependencyFlow.Tests;

[TestFixture]
public class PullRequestPolicyFailureNotifierTests
{
    [Test]
    public async Task NotifierConnectionErrorsAreSilent()
    {
        var mockClient = new Mock<IPullRequestPolicyFailureNotificationClient>();
        mockClient.Setup(m => m.SendPolicyCheckFailedNotificationAsync(It.IsAny<string[]>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Connection Error"));

        var notifier = new PullRequestPolicyFailureNotifier(mockClient.Object, Mock.Of<IPullRequestPolicyProvider>(), new NullLogger<PullRequestPolicyFailureNotifier>());

        // Should not throw
        await notifier.SendPullRequestFailureNotificationAsync(
            "https://github.com/dotnet/arcade", "commit", "repo", new BuildRef(1, true, 0), CancellationToken.None);
    }

    [Test]
    public async Task NotificationIsSentWithTags()
    {
        string[] tags = null;
        string url = null;
        string message = null;

        var mockClient = new Mock<IPullRequestPolicyFailureNotificationClient>();
        mockClient.Setup(m => m.SendPolicyCheckFailedNotificationAsync(It.IsAny<string[]>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string[], string, string, CancellationToken>((t, u, m, _) =>
            {
                tags = t;
                url = u;
                message = m;
            })
            .Returns(Task.CompletedTask);

        var mockPullRequestInfo = new Mock<IPullRequestPolicyProvider>();
        mockPullRequestInfo.Setup(m => m.GetPullRequestUrlAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("https://github.com/dotnet/arcade/pull/123");

        var notifier = new PullRequestPolicyFailureNotifier(mockClient.Object, mockPullRequestInfo.Object, new NullLogger<PullRequestPolicyFailureNotifier>());

        await notifier.SendPullRequestFailureNotificationAsync(
            "https://github.com/dotnet/arcade", "commit", "repo", new BuildRef(1, true, 0, "tag1,tag2"), CancellationToken.None);

        tags.ShouldBe(new[] { "tag1", "tag2" });
        url.ShouldBe("https://github.com/dotnet/arcade/pull/123");
        message.ShouldNotBeNull();
    }

    [Test]
    public async Task NoFailureNotificationIfNoPullRequestTagsOrUrl()
    {
        // No tags defined
        var mockClient = new Mock<IPullRequestPolicyFailureNotificationClient>();
        var mockPullRequestInfo = new Mock<IPullRequestPolicyProvider>();
        var notifier = new PullRequestPolicyFailureNotifier(mockClient.Object, mockPullRequestInfo.Object, new NullLogger<PullRequestPolicyFailureNotifier>());

        await notifier.SendPullRequestFailureNotificationAsync(
            "https://github.com/dotnet/arcade", "commit", "repo", new BuildRef(1, true, 0), CancellationToken.None);

        mockPullRequestInfo.Verify(m => m.GetPullRequestUrlAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        mockClient.Verify(m => m.SendPolicyCheckFailedNotificationAsync(It.IsAny<string[]>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);

        // Empty notification tags
        mockPullRequestInfo.Reset();
        mockClient.Reset();
        await notifier.SendPullRequestFailureNotificationAsync(
            "https://github.com/dotnet/arcade", "commit", "repo", new BuildRef(1, true, 0, ""), CancellationToken.None);

        mockPullRequestInfo.Verify(m => m.GetPullRequestUrlAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        mockClient.Verify(m => m.SendPolicyCheckFailedNotificationAsync(It.IsAny<string[]>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);

        // No PR URL found (e.g. commit was direct to the target branch)
        mockPullRequestInfo.Reset();
        mockClient.Reset();
        mockPullRequestInfo
            .Setup(m => m.GetPullRequestUrlAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string)null);

        await notifier.SendPullRequestFailureNotificationAsync(
            "https://github.com/dotnet/arcade", "commit", "repo", new BuildRef(1, true, 0, "tag"), CancellationToken.None);

        mockPullRequestInfo.Verify(m => m.GetPullRequestUrlAsync("https://github.com/dotnet/arcade", "commit", It.IsAny<CancellationToken>()), Times.Once);
        mockClient.Verify(m => m.SendPolicyCheckFailedNotificationAsync(It.IsAny<string[]>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
