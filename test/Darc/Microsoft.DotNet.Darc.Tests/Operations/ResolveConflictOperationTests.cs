// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.DotNet.Darc.Operations.VirtualMonoRepo;
using Microsoft.DotNet.Darc.Options.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using Microsoft.DotNet.ProductConstructionService.Client.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;

namespace Microsoft.DotNet.Darc.Tests.Operations;

[TestFixture]
public class ResolveConflictOperationTests
{
    private const string OldSha = "1111111111111111111111111111111111111111";
    private const string NewSha = "2222222222222222222222222222222222222222";
    private const string Remote = "https://github.com/dotnet/test";
    private readonly ProcessManager _processManager = new(NullLogger.Instance, "git");
    private string _tempDirectory;

    [SetUp]
    public void Setup()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), $"darc resolve conflict {Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDirectory);
    }

    [TearDown]
    public void TearDown()
    {
        if (!Directory.Exists(_tempDirectory))
        {
            return;
        }

        // Git object files can be read-only on Windows.
        foreach (var file in new DirectoryInfo(_tempDirectory).GetFiles("*", SearchOption.AllDirectories))
        {
            file.Attributes = FileAttributes.Normal;
        }

        Directory.Delete(_tempDirectory, recursive: true);
    }

    [TestCase(false, true)]
    [TestCase(false, false)]
    [TestCase(true, true)]
    [TestCase(true, false)]
    public async Task CreateCommitMessageFileAsync_WritesToTargetGitDirectoryAsync(bool useWorktree, bool forwardFlow)
    {
        // Arrange
        string repository = Path.Combine(_tempDirectory, "parent repository");
        Directory.CreateDirectory(repository);
        await GitAsync(repository, "init", "--initial-branch=main");
        await GitAsync(repository,
            "-c", "user.name=Darc Test", "-c", "user.email=darc-test@example.com",
            "-c", "commit.gpgsign=false", "-c", $"core.hooksPath={_tempDirectory}",
            "commit", "--allow-empty", "-m", "Initial commit");

        string parentMessagePath = Path.Combine(repository, ".git", "SQUASH_MSG");
        const string originalMessage = "Existing squash message";
        File.WriteAllText(parentMessagePath, originalMessage);
        string targetRepository = repository;
        string targetMessagePath = parentMessagePath;

        if (useWorktree)
        {
            targetRepository = Path.Combine(_tempDirectory, "linked worktree");
            await GitAsync(repository, "worktree", "add", "-b", "conflict-resolution", targetRepository);
            File.Exists(Path.Combine(targetRepository, ".git")).Should().BeTrue();

            // Read the worktree's gitdir independently of the rev-parse command under test.
            string gitDirectory = File.ReadAllText(Path.Combine(targetRepository, ".git"))["gitdir: ".Length..].Trim();
            Path.IsPathFullyQualified(gitDirectory).Should().BeTrue();
            targetMessagePath = Path.Combine(gitDirectory, "SQUASH_MSG");
            File.WriteAllText(targetMessagePath, "Worktree squash message to overwrite");
        }

        var operation = CreateOperation(new FileSystem());
        var subscription = CreateSubscription(forwardFlow);

        // Act
        await CreateCommitMessageFileAsync(operation, targetRepository, subscription);

        // Assert
        string expectedMessage = string.Join(Environment.NewLine,
            $"[test-mapping] Source update 1111111{DarcLib.Constants.Arrow}2222222",
            $"Diff: {Remote}/compare/{OldSha}..{NewSha}",
            "",
            $"From: {Remote}/commit/{OldSha}",
            $"To: {Remote}/commit/{NewSha}",
            "",
            "The following files had conflicts that were resolved by a user:",
            "",
            "- src/file with spaces.cs",
            "- eng/Version.Details.xml",
            "");
        TestHelpers.NormalizeLineEndings(File.ReadAllText(targetMessagePath)).Should().Be(expectedMessage);

        if (useWorktree)
        {
            File.ReadAllText(parentMessagePath).Should().Be(originalMessage);
            File.Exists(Path.Combine(targetRepository, "SQUASH_MSG")).Should().BeFalse();
        }
    }

    [Test]
    public async Task CreateCommitMessageFileAsync_WhenGitFails_DoesNotWriteAsync()
    {
        // Arrange: an existing directory that is not a git repository.
        var fileSystem = new Mock<IFileSystem>(MockBehavior.Strict);
        var operation = CreateOperation(fileSystem.Object);

        // Act
        Func<Task> act = () => CreateCommitMessageFileAsync(operation, _tempDirectory, CreateSubscription(true));

        // Assert
        var exception = await act.Should().ThrowAsync<ProcessFailedException>()
            .WithMessage("Failed to resolve the squash commit message path.*");
        exception.Which.ExecutionResult.ExitCode.Should().Be(128);
        exception.Which.ExecutionResult.StandardError.Should().NotBeNullOrWhiteSpace();
        fileSystem.VerifyNoOtherCalls();
        Directory.GetFileSystemEntries(_tempDirectory).Should().BeEmpty();
    }

    private ResolveConflictOperation CreateOperation(IFileSystem fileSystem) => new(
        new ResolveConflictCommandLineOptions(),
        Mock.Of<IVmrForwardFlower>(),
        Mock.Of<IVmrBackFlower>(),
        Mock.Of<IVmrInfo>(),
        Mock.Of<IVmrCloneManager>(),
        Mock.Of<IRepositoryCloneManager>(),
        Mock.Of<IVmrDependencyTracker>(),
        Mock.Of<IDependencyFileManager>(),
        Mock.Of<ILocalGitRepoFactory>(),
        Mock.Of<IBarApiClient>(),
        Mock.Of<ILocalFactory>(),
        fileSystem,
        _processManager,
        NullLogger<ResolveConflictOperation>.Instance);

    private static Subscription CreateSubscription(bool forwardFlow) => new(
        Guid.Empty, true, true, Remote, "https://github.com/dotnet/target", "main",
        forwardFlow ? null : "test-mapping",
        forwardFlow ? "test-mapping" : null,
        null, []);

    private static async Task CreateCommitMessageFileAsync(
        ResolveConflictOperation operation,
        string repository,
        Subscription subscription)
    {
        var build = new Build(
            id: 1, dateProduced: DateTimeOffset.UnixEpoch, staleness: 0,
            released: false, stable: false, commit: NewSha,
            channels: [], assets: [], dependencies: [], incoherencies: []);

        // Isolate the path regression without setting up unrelated cloning and codeflow metadata.
        var method = typeof(ResolveConflictOperation).GetMethod(
            "CreateCommitMessageFileAsync", BindingFlags.Instance | BindingFlags.NonPublic);
        method.Should().NotBeNull();
        await (Task)method.Invoke(operation,
            [repository, subscription, build, OldSha,
                new UnixPath[] { new("src/file with spaces.cs"), new("eng/Version.Details.xml") },
                CancellationToken.None]);
    }

    private async Task GitAsync(string repository, params string[] arguments)
    {
        var result = await _processManager.ExecuteGit(repository, arguments);
        result.Succeeded.Should().BeTrue(result.ToString());
    }
}
