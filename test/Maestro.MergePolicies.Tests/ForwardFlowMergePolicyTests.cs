// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using AwesomeAssertions;
using Maestro.MergePolicyEvaluation;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Models.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Maestro.MergePolicies.Tests;

[TestFixture]
public class ForwardFlowMergePolicyTests
{
    private const string RepoUrl = "https://github.com/test/vmr";
    private const string HeadBranch = "pr-branch";
    private const string TargetBranch = "main";
    private const string SourceRepo1 = "https://github.com/test/repo1";
    private const string SourceRepo2 = "https://github.com/test/repo2";
    private const string SourceRepo3 = "https://github.com/test/repo3";
    private const string CommitSha1 = "abc123";
    private const string CommitSha2 = "def456";
    private const string CommitSha3 = "ghi789";
    private const string UpdatedCommitSha = "updated123";
    private const int BarId1 = 100;
    private const int BarId2 = 200;
    private const int BarId3 = 300;
    private const int UpdatedBarId = 101;

    private Mock<IRemote> _mockRemote = null!;
    private ForwardFlowMergePolicy _policy = null!;

    [SetUp]
    public void Setup()
    {
        _mockRemote = new Mock<IRemote>(MockBehavior.Strict);
        _policy = new ForwardFlowMergePolicy(NullLogger<IMergePolicy>.Instance);
    }

    private static PullRequestUpdateSummary CreatePrSummary(
        string sourceRepo,
        int buildId,
        string commitSha,
        string? targetBranch = TargetBranch)
    {
        return new PullRequestUpdateSummary(
            url: "https://github.com/test/vmr/pull/123",
            coherencyCheckSuccessful: null,
            coherencyErrors: [],
            requiredUpdates: [],
            containedUpdates:
            [
                new SubscriptionUpdateSummary(Guid.NewGuid(), buildId, sourceRepo, commitSha)
            ],
            headBranch: HeadBranch,
            targetBranch: targetBranch,
            repoUrl: RepoUrl,
            codeFlowDirection: CodeFlowDirection.ForwardFlow);
    }

    private static SourceManifest CreateSourceManifest(params (string remoteUri, string commitSha, int? barId)[] repos)
    {
        var records = repos.Select((r, i) => new RepositoryRecord($"repo{i}", r.remoteUri, r.commitSha, r.barId));
        return new SourceManifest(records, []);
    }

    [Test]
    public async Task EvaluateAsync_WhenAllChecksPass_ShouldSucceed()
    {
        // Arrange
        var prSummary = CreatePrSummary(SourceRepo1, UpdatedBarId, UpdatedCommitSha);

        var headManifest = CreateSourceManifest(
            (SourceRepo1, UpdatedCommitSha, UpdatedBarId),
            (SourceRepo2, CommitSha2, BarId2));

        var targetManifest = CreateSourceManifest(
            (SourceRepo1, CommitSha1, BarId1),
            (SourceRepo2, CommitSha2, BarId2));

        _mockRemote.Setup(r => r.GetSourceManifestAsync(RepoUrl, HeadBranch))
            .ReturnsAsync(headManifest);
        _mockRemote.Setup(r => r.GetSourceManifestAsync(RepoUrl, TargetBranch))
            .ReturnsAsync(targetManifest);

        // Act
        var result = await _policy.EvaluateAsync(prSummary, _mockRemote.Object);

        // Assert
        result.Status.Should().Be(MergePolicyEvaluationStatus.DecisiveSuccess);
        result.Title.Should().Contain("Forward flow checks succeeded");
    }

    [Test]
    public async Task EvaluateAsync_WhenBarIdMismatch_ShouldFail()
    {
        // Arrange - head manifest has wrong BAR ID for the updated repo
        var prSummary = CreatePrSummary(SourceRepo1, UpdatedBarId, UpdatedCommitSha);

        var headManifest = CreateSourceManifest(
            (SourceRepo1, UpdatedCommitSha, BarId1), // Wrong BAR ID - should be UpdatedBarId
            (SourceRepo2, CommitSha2, BarId2));

        _mockRemote.Setup(r => r.GetSourceManifestAsync(RepoUrl, HeadBranch))
            .ReturnsAsync(headManifest);
        _mockRemote.Setup(r => r.GetSourceManifestAsync(RepoUrl, TargetBranch))
            .ReturnsAsync(headManifest);

        // Act
        var result = await _policy.EvaluateAsync(prSummary, _mockRemote.Object);

        // Assert
        result.Status.Should().Be(MergePolicyEvaluationStatus.DecisiveFailure);
        result.Message.Should().Contain("BAR ID");
        result.Message.Should().Contain("does not match");
    }

    [Test]
    public async Task EvaluateAsync_WhenCommitShaMismatch_ShouldFail()
    {
        // Arrange - head manifest has wrong commit SHA for the updated repo
        var prSummary = CreatePrSummary(SourceRepo1, UpdatedBarId, UpdatedCommitSha);

        var headManifest = CreateSourceManifest(
            (SourceRepo1, CommitSha1, UpdatedBarId), // Wrong commit SHA - should be UpdatedCommitSha
            (SourceRepo2, CommitSha2, BarId2));

        _mockRemote.Setup(r => r.GetSourceManifestAsync(RepoUrl, HeadBranch))
            .ReturnsAsync(headManifest);
        _mockRemote.Setup(r => r.GetSourceManifestAsync(RepoUrl, TargetBranch))
            .ReturnsAsync(headManifest);

        // Act
        var result = await _policy.EvaluateAsync(prSummary, _mockRemote.Object);

        // Assert
        result.Status.Should().Be(MergePolicyEvaluationStatus.DecisiveFailure);
        result.Message.Should().Contain("Commit SHA");
        result.Message.Should().Contain("does not match");
    }

    [Test]
    public async Task EvaluateAsync_WhenUnrelatedRepoChanged_ShouldFail()
    {
        // Arrange - repo2 changed even though only repo1 is being updated
        var prSummary = CreatePrSummary(SourceRepo1, UpdatedBarId, UpdatedCommitSha);

        var headManifest = CreateSourceManifest(
            (SourceRepo1, UpdatedCommitSha, UpdatedBarId),
            (SourceRepo2, "changed-sha", BarId2)); // Changed!

        var targetManifest = CreateSourceManifest(
            (SourceRepo1, CommitSha1, BarId1),
            (SourceRepo2, CommitSha2, BarId2));

        _mockRemote.Setup(r => r.GetSourceManifestAsync(RepoUrl, HeadBranch))
            .ReturnsAsync(headManifest);
        _mockRemote.Setup(r => r.GetSourceManifestAsync(RepoUrl, TargetBranch))
            .ReturnsAsync(targetManifest);

        // Act
        var result = await _policy.EvaluateAsync(prSummary, _mockRemote.Object);

        // Assert
        result.Status.Should().Be(MergePolicyEvaluationStatus.DecisiveFailure);
        result.Message.Should().Contain(SourceRepo2);
        result.Message.Should().Contain("Only changes to the");
    }

    [Test]
    public async Task EvaluateAsync_WhenNewRepoAdded_ShouldFail()
    {
        // Arrange - repo3 added in head that doesn't exist in target
        var prSummary = CreatePrSummary(SourceRepo1, UpdatedBarId, UpdatedCommitSha);

        var headManifest = CreateSourceManifest(
            (SourceRepo1, UpdatedCommitSha, UpdatedBarId),
            (SourceRepo2, CommitSha2, BarId2),
            (SourceRepo3, CommitSha3, BarId3)); // New repo added!

        var targetManifest = CreateSourceManifest(
            (SourceRepo1, CommitSha1, BarId1),
            (SourceRepo2, CommitSha2, BarId2));

        _mockRemote.Setup(r => r.GetSourceManifestAsync(RepoUrl, HeadBranch))
            .ReturnsAsync(headManifest);
        _mockRemote.Setup(r => r.GetSourceManifestAsync(RepoUrl, TargetBranch))
            .ReturnsAsync(targetManifest);

        // Act
        var result = await _policy.EvaluateAsync(prSummary, _mockRemote.Object);

        // Assert
        result.Status.Should().Be(MergePolicyEvaluationStatus.DecisiveFailure);
        result.Message.Should().Contain(SourceRepo3);
        result.Message.Should().Contain("new entry");
    }

    [Test]
    public async Task EvaluateAsync_WhenRepoDeleted_ShouldFail()
    {
        // Arrange - repo2 deleted from head
        var prSummary = CreatePrSummary(SourceRepo1, UpdatedBarId, UpdatedCommitSha);

        var headManifest = CreateSourceManifest(
            (SourceRepo1, UpdatedCommitSha, UpdatedBarId));
        // repo2 is missing!

        var targetManifest = CreateSourceManifest(
            (SourceRepo1, CommitSha1, BarId1),
            (SourceRepo2, CommitSha2, BarId2));

        _mockRemote.Setup(r => r.GetSourceManifestAsync(RepoUrl, HeadBranch))
            .ReturnsAsync(headManifest);
        _mockRemote.Setup(r => r.GetSourceManifestAsync(RepoUrl, TargetBranch))
            .ReturnsAsync(targetManifest);

        // Act
        var result = await _policy.EvaluateAsync(prSummary, _mockRemote.Object);

        // Assert
        result.Status.Should().Be(MergePolicyEvaluationStatus.DecisiveFailure);
        result.Message.Should().Contain(SourceRepo2);
        result.Message.Should().Contain("removal");
    }

    [Test]
    public async Task EvaluateAsync_WhenHeadManifestFetchFails_ShouldFailTransiently()
    {
        // Arrange
        var prSummary = CreatePrSummary(SourceRepo1, UpdatedBarId, UpdatedCommitSha);

        _mockRemote.Setup(r => r.GetSourceManifestAsync(RepoUrl, HeadBranch))
            .ThrowsAsync(new Exception("Network error"));

        // Act
        var result = await _policy.EvaluateAsync(prSummary, _mockRemote.Object);

        // Assert
        result.Status.Should().Be(MergePolicyEvaluationStatus.TransientFailure);
        result.Title.Should().Contain("Error while retrieving head branch source manifest");
    }

    [Test]
    public async Task EvaluateAsync_WhenTargetManifestFetchFails_ShouldFailTransiently()
    {
        // Arrange
        var prSummary = CreatePrSummary(SourceRepo1, UpdatedBarId, UpdatedCommitSha);

        var headManifest = CreateSourceManifest(
            (SourceRepo1, UpdatedCommitSha, UpdatedBarId),
            (SourceRepo2, CommitSha2, BarId2));

        _mockRemote.Setup(r => r.GetSourceManifestAsync(RepoUrl, HeadBranch))
            .ReturnsAsync(headManifest);
        _mockRemote.Setup(r => r.GetSourceManifestAsync(RepoUrl, TargetBranch))
            .ThrowsAsync(new Exception("Network error"));

        // Act
        var result = await _policy.EvaluateAsync(prSummary, _mockRemote.Object);

        // Assert
        result.Status.Should().Be(MergePolicyEvaluationStatus.TransientFailure);
        result.Title.Should().Contain("Error while retrieving target branch source manifest");
    }

    [Test]
    public async Task EvaluateAsync_WhenNoTargetBranch_ShouldSkipRepoChangeValidation()
    {
        // Arrange - no target branch means we can't compare manifests
        var prSummary = CreatePrSummary(SourceRepo1, UpdatedBarId, UpdatedCommitSha, targetBranch: null);

        var headManifest = CreateSourceManifest(
            (SourceRepo1, UpdatedCommitSha, UpdatedBarId),
            (SourceRepo2, CommitSha2, BarId2));

        _mockRemote.Setup(r => r.GetSourceManifestAsync(RepoUrl, HeadBranch))
            .ReturnsAsync(headManifest);
        // Target manifest should not be fetched

        // Act
        var result = await _policy.EvaluateAsync(prSummary, _mockRemote.Object);

        // Assert
        result.Status.Should().Be(MergePolicyEvaluationStatus.DecisiveSuccess);
        _mockRemote.Verify(r => r.GetSourceManifestAsync(RepoUrl, It.IsAny<string>()), Times.Once);
    }
}
