// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using AwesomeAssertions;
using Maestro.Common;
using Maestro.Common.Telemetry;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.Internal.Credentials;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;

namespace ProductConstructionService.DependencyFlow.Tests;

internal class ServiceCommitTrackerTests
{
    private static readonly NativePath RepositoryPath = new("D:\\repo");
    private const string Branch = "pr-branch";

    private ServiceCommitTracker _tracker = null!;

    [SetUp]
    public void SetUp()
    {
        _tracker = new ServiceCommitTracker();
    }

    [Test]
    public async Task GetReachableCommitsAsyncReturnsTrackedCommitsReachableFromBranch()
    {
        // Arrange
        const string commit = "tracked-commit";
        _tracker.TrackCommit(RepositoryPath, commit);

        Mock<ILocalGitClient> gitClient = new();
        gitClient
            .Setup(client => client.IsAncestorCommit(RepositoryPath, commit, Branch))
            .ReturnsAsync(true);

        // Act
        List<string> commits = await _tracker.GetReachableCommitsAsync(gitClient.Object, RepositoryPath, Branch, []);

        // Assert
        commits.Should().Equal(commit);
    }

    [Test]
    public async Task ReplaceCommitSwapsTheTrackedCommit()
    {
        // Arrange
        const string originalCommit = "original-commit";
        const string amendedCommit = "amended-commit";
        _tracker.TrackCommit(RepositoryPath, originalCommit);
        _tracker.ReplaceCommit(RepositoryPath, originalCommit, amendedCommit);

        Mock<ILocalGitClient> gitClient = new();
        gitClient
            .Setup(client => client.IsAncestorCommit(RepositoryPath, It.IsAny<string>(), Branch))
            .ReturnsAsync(true);

        // Act
        List<string> commits = await _tracker.GetReachableCommitsAsync(gitClient.Object, RepositoryPath, Branch, []);

        // Assert
        commits.Should().Equal(amendedCommit);
        gitClient.Verify(
            client => client.IsAncestorCommit(RepositoryPath, originalCommit, Branch),
            Times.Never);
    }

    [Test]
    public async Task GetReachableCommitsAsyncKeepsOnlyServiceCommitsOnFinalBranch()
    {
        // Arrange
        const string previousServiceCommit = "previous-service-commit";
        const string temporaryWorkBranchCommit = "temporary-work-branch-commit";
        const string finalServiceCommit = "final-service-commit";
        _tracker.TrackCommit(RepositoryPath, temporaryWorkBranchCommit);
        _tracker.TrackCommit(RepositoryPath, finalServiceCommit);

        Mock<ILocalGitClient> gitClient = new();
        gitClient
            .Setup(client => client.IsAncestorCommit(RepositoryPath, It.IsAny<string>(), Branch))
            .ReturnsAsync((string _, string commit, string _) => commit != temporaryWorkBranchCommit);

        // Act
        List<string> commits = await _tracker.GetReachableCommitsAsync(
            gitClient.Object,
            RepositoryPath,
            Branch,
            [previousServiceCommit]);

        // Assert
        commits.Should().Equal(previousServiceCommit, finalServiceCommit);
    }
}

internal abstract class TrackingGitClientTestsBase
{
    protected const string RepoPath = "D:\\repo";

    private Mock<IServiceCommitTracker> _tracker = null!;
    private Mock<IProcessManager> _processManager = null!;
    private Queue<string> _revParseResults = null!;
    private ILocalGitClient _client = null!;

    protected abstract ILocalGitClient CreateClient(
        IServiceCommitTracker commitTracker,
        IProcessManager processManager);

    [SetUp]
    public void SetUp()
    {
        _tracker = new();
        _processManager = new();
        _revParseResults = new();

        // git commit builds its arguments as an IEnumerable<string>, while rev-parse / git commit --amend
        // pass a string[]; Moq intercepts each overload separately, so both must be set up. The responder
        // drives the result off the first argument ('commit' succeeds, 'rev-parse' returns the queued SHA).
        _processManager
            .Setup(m => m.ExecuteGit(
                RepoPath,
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<Dictionary<string, string>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, IEnumerable<string> args, Dictionary<string, string>? _, CancellationToken _) => Respond(args.ToArray()));
        _processManager
            .Setup(m => m.ExecuteGit(
                RepoPath,
                It.IsAny<string[]>(),
                It.IsAny<Dictionary<string, string>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, string[] args, Dictionary<string, string>? _, CancellationToken _) => Respond(args));

        _client = CreateClient(_tracker.Object, _processManager.Object);
    }

    [Test]
    public async Task CommitAsyncTracksTheCreatedCommit()
    {
        // Arrange
        const string createdCommit = "created-commit";
        _revParseResults.Enqueue(createdCommit);

        // Act
        await _client.CommitAsync(RepoPath, "message", allowEmpty: true);

        // Assert
        _tracker.Verify(
            t => t.TrackCommit(It.Is<NativePath>(p => p == RepoPath), createdCommit),
            Times.Once);
    }

    [Test]
    public async Task CommitAmendAsyncReplacesTheAmendedCommit()
    {
        // Arrange
        const string originalCommit = "original-commit";
        const string amendedCommit = "amended-commit";
        _revParseResults.Enqueue(originalCommit);
        _revParseResults.Enqueue(amendedCommit);

        // Act
        await _client.CommitAmendAsync(RepoPath);

        // Assert
        _tracker.Verify(
            t => t.ReplaceCommit(It.Is<NativePath>(p => p == RepoPath), originalCommit, amendedCommit),
            Times.Once);
    }

    private ProcessExecutionResult Respond(string[] args)
        => args[0] == "rev-parse"
            ? Success(_revParseResults.Dequeue())
            : Success(string.Empty);

    private static ProcessExecutionResult Success(string standardOutput)
        => new() { ExitCode = 0, StandardOutput = standardOutput };
}

internal class TrackingLocalGitClientTests : TrackingGitClientTestsBase
{
    protected override ILocalGitClient CreateClient(IServiceCommitTracker commitTracker, IProcessManager processManager)
        => new TrackingLocalGitClient(
            commitTracker,
            Mock.Of<IRemoteTokenProvider>(),
            Mock.Of<ITelemetryRecorder>(),
            processManager,
            Mock.Of<IFileSystem>(),
            NullLogger<TrackingLocalGitClient>.Instance);
}

internal class TrackingLocalLibGit2ClientTests : TrackingGitClientTestsBase
{
    protected override ILocalGitClient CreateClient(IServiceCommitTracker commitTracker, IProcessManager processManager)
        => new TrackingLocalLibGit2Client(
            commitTracker,
            Mock.Of<IRemoteTokenProvider>(),
            Mock.Of<ITelemetryRecorder>(),
            processManager,
            Mock.Of<IFileSystem>(),
            NullLogger<TrackingLocalLibGit2Client>.Instance);
}
