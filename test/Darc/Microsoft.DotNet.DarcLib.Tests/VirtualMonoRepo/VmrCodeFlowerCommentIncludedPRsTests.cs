// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.Models.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;

#nullable enable
namespace Microsoft.DotNet.DarcLib.Tests.VirtualMonoRepo;

[TestFixture]
public class VmrCodeFlowerCommentIncludedPRsTests
{
    private const string RepoUri = "https://github.com/dotnet/test-repo";
    private const string LocalRepoUri = "/data/local-repo";
    private const string LastCommit = "last-sha";
    private const string CurrentCommit = "current-sha";
    private const string CommentHeader = "PRs included in this flow:";
    private const string PathFilter = "src/test-repo";
    private const string OtherSummary = "<summary>Other PRs in the commit range</summary>";

    private static readonly string[] FullRangeArgs = ["log", "--pretty=%s", $"{LastCommit}..{CurrentCommit}"];
    private static readonly string[] FilteredArgs = ["log", "--pretty=%s", $"{LastCommit}..{CurrentCommit}", "--", PathFilter];

    private Mock<ILocalGitRepo> _repo = null!;
    private CommentCollector _commentCollector = null!;
    private CancellationTokenSource _cancellationTokenSource = null!;
    private TestVmrCodeFlower _codeFlower = null!;

    [SetUp]
    public void SetUp()
    {
        _repo = new Mock<ILocalGitRepo>();
        _repo.SetupGet(x => x.Path).Returns(new NativePath("/data/repo"));

        _commentCollector = new CommentCollector();
        _cancellationTokenSource = new CancellationTokenSource();

        _codeFlower = new TestVmrCodeFlower(_commentCollector);
    }

    [TearDown]
    public void TearDown()
    {
        _cancellationTokenSource.Dispose();
    }

    [Test]
    public async Task CommentIncludedPRsAsync_NullPathFilter_PostsSuppliedHeaderWithPrsInReverseOrder()
    {
        // Arrange
        SetupGitLog(FullRangeArgs,
            "Merge pull request #3 from dotnet/feature",
            "Commit without a PR reference",
            "Fix build (#2)",
            "Update dependencies (#1)");

        // Act
        await CommentIncludedPRsAsync(pathFilter: null);

        // Assert
        VerifySingleComment(
            CommentHeader,
            $"- {RepoUri}/pull/1",
            $"- {RepoUri}/pull/2",
            $"- {RepoUri}/pull/3");
        VerifyGitLogCalled(FullRangeArgs, Times.Once());
        _repo.Verify(x => x.ExecuteGitCommand(It.IsAny<string[]>(), It.IsAny<CancellationToken>()), Times.Once());
    }

    [Test]
    public async Task CommentIncludedPRsAsync_NullPathFilter_DeduplicatesIdenticalTitlesOnly()
    {
        // Arrange
        // Existing behavior: deduplication is by (title, URI), so the same PR with different titles is listed twice.
        SetupGitLog(FullRangeArgs,
            "Fix build (#2)",
            "Update dependencies (#1)",
            "Update dependencies (#1)",
            "Update dependencies again (#1)");

        // Act
        await CommentIncludedPRsAsync(pathFilter: null);

        // Assert
        VerifySingleComment(
            CommentHeader,
            $"- {RepoUri}/pull/1",
            $"- {RepoUri}/pull/1",
            $"- {RepoUri}/pull/2");
    }

    [Test]
    public async Task CommentIncludedPRsAsync_PathFilter_GroupsMatchingAndOtherPrs()
    {
        // Arrange
        SetupGitLog(FullRangeArgs,
            "Merge pull request #4 from dotnet/feature",
            "Update src (#3)",
            "Commit without a PR reference",
            "Fix docs (#2)",
            "Update src again (#1)");
        SetupGitLog(FilteredArgs,
            "Update src (#3)",
            "Update src again (#1)");

        // Act
        await CommentIncludedPRsAsync(PathFilter);

        // Assert
        VerifySingleComment(
            CommentHeader,
            $"- {RepoUri}/pull/1",
            $"- {RepoUri}/pull/3",
            string.Empty,
            "<details>",
            OtherSummary,
            string.Empty,
            $"- {RepoUri}/pull/2",
            $"- {RepoUri}/pull/4",
            string.Empty,
            "</details>");
        VerifyGitLogCalled(FullRangeArgs, Times.Once());
        VerifyGitLogCalled(FilteredArgs, Times.Once());
        _repo.Verify(x => x.ExecuteGitCommand(It.IsAny<string[]>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Test]
    public async Task CommentIncludedPRsAsync_PathFilter_PrReferencedByDistinctTitles_IsListedOnce()
    {
        // Arrange
        SetupGitLog(FullRangeArgs,
            "Revert part of change (#5)",
            "Fix docs (#2)",
            "Follow-up for src (#5)",
            "Update docs (#2)",
            "Update src (#5)");
        SetupGitLog(FilteredArgs,
            "Follow-up for src (#5)",
            "Update src (#5)");

        // Act
        await CommentIncludedPRsAsync(PathFilter);

        // Assert
        VerifySingleComment(
            CommentHeader,
            $"- {RepoUri}/pull/5",
            string.Empty,
            "<details>",
            OtherSummary,
            string.Empty,
            $"- {RepoUri}/pull/2",
            string.Empty,
            "</details>");
    }

    [Test]
    public async Task CommentIncludedPRsAsync_PathFilter_AllPrsMatch_OmitsOtherGroup()
    {
        // Arrange
        SetupGitLog(FullRangeArgs,
            "Commit without a PR reference",
            "Update src (#3)",
            "Update src again (#1)");
        SetupGitLog(FilteredArgs,
            "Update src (#3)",
            "Update src again (#1)");

        // Act
        await CommentIncludedPRsAsync(PathFilter);

        // Assert
        VerifySingleComment(
            CommentHeader,
            $"- {RepoUri}/pull/1",
            $"- {RepoUri}/pull/3");
    }

    [Test]
    public async Task CommentIncludedPRsAsync_PathFilter_NoMatchingPrs_OmitsMatchingGroup()
    {
        // Arrange
        SetupGitLog(FullRangeArgs,
            "Merge pull request #4 from dotnet/feature",
            "Fix docs (#2)");
        SetupGitLog(FilteredArgs, "Commit without a PR reference");

        // Act
        await CommentIncludedPRsAsync(PathFilter);

        // Assert
        VerifySingleComment(
            "<details>",
            OtherSummary,
            string.Empty,
            $"- {RepoUri}/pull/2",
            $"- {RepoUri}/pull/4",
            string.Empty,
            "</details>");
    }

    [TestCase(null)]
    [TestCase(PathFilter)]
    public async Task CommentIncludedPRsAsync_NoPrs_DoesNotComment(string? pathFilter)
    {
        // Arrange
        SetupGitLog(FullRangeArgs, "Commit without a PR reference");
        SetupGitLog(FilteredArgs);

        // Act
        await CommentIncludedPRsAsync(pathFilter);

        // Assert
        _commentCollector.GetComments().Should().BeEmpty();
    }

    [TestCase(null)]
    [TestCase(PathFilter)]
    public async Task CommentIncludedPRsAsync_LocalRepoUri_SkipsGitAndComment(string? pathFilter)
    {
        // Act
        await CommentIncludedPRsAsync(pathFilter, LocalRepoUri);

        // Assert
        _repo.Verify(x => x.ExecuteGitCommand(It.IsAny<string[]>(), It.IsAny<CancellationToken>()), Times.Never());
        _commentCollector.GetComments().Should().BeEmpty();
    }

    [TestCase(null)]
    [TestCase(PathFilter)]
    public async Task CommentIncludedPRsAsync_FullRangeGitLogFails_Throws(string? pathFilter)
    {
        // Arrange
        SetupFailedGitLog(FullRangeArgs);
        SetupGitLog(FilteredArgs, "Update src (#1)");

        // Act
        Func<Task> act = () => CommentIncludedPRsAsync(pathFilter);

        // Assert
        await act.Should().ThrowAsync<ProcessFailedException>();
        _commentCollector.GetComments().Should().BeEmpty();
    }

    [Test]
    public async Task CommentIncludedPRsAsync_FilteredGitLogFails_Throws()
    {
        // Arrange
        SetupGitLog(FullRangeArgs, "Update src (#1)");
        SetupFailedGitLog(FilteredArgs);

        // Act
        Func<Task> act = () => CommentIncludedPRsAsync(PathFilter);

        // Assert
        await act.Should().ThrowAsync<ProcessFailedException>();
        _commentCollector.GetComments().Should().BeEmpty();
    }

    private Task CommentIncludedPRsAsync(string? pathFilter, string repoUri = RepoUri)
        => _codeFlower.CommentIncludedPRsAsync(
            _repo.Object,
            LastCommit,
            CurrentCommit,
            repoUri,
            CommentHeader,
            pathFilter,
            _cancellationTokenSource.Token);

    private void SetupGitLog(string[] expectedArgs, params string[] outputLines)
    {
        _repo
            .Setup(x => x.ExecuteGitCommand(
                It.Is<string[]>(args => args.SequenceEqual(expectedArgs)),
                _cancellationTokenSource.Token))
            .ReturnsAsync(new ProcessExecutionResult
            {
                ExitCode = 0,
                StandardOutput = string.Join(Environment.NewLine, outputLines),
            });
    }

    private void SetupFailedGitLog(string[] expectedArgs)
    {
        _repo
            .Setup(x => x.ExecuteGitCommand(
                It.Is<string[]>(args => args.SequenceEqual(expectedArgs)),
                _cancellationTokenSource.Token))
            .ReturnsAsync(new ProcessExecutionResult
            {
                ExitCode = 128,
                StandardError = "fatal: bad revision",
            });
    }

    private void VerifyGitLogCalled(string[] expectedArgs, Times times)
    {
        _repo.Verify(
            x => x.ExecuteGitCommand(
                It.Is<string[]>(args => args.SequenceEqual(expectedArgs)),
                _cancellationTokenSource.Token),
            times);
    }

    private void VerifySingleComment(params string[] expectedLines)
    {
        _commentCollector.GetComments().Should().Equal(
            new Comment(string.Join(Environment.NewLine, expectedLines), CommentType.Information));
    }

    private sealed class TestVmrCodeFlower : VmrCodeFlower
    {
        public TestVmrCodeFlower(ICommentCollector commentCollector)
            : base(
                Mock.Of<IVmrInfo>(),
                Mock.Of<ISourceManifest>(),
                Mock.Of<IVmrDependencyTracker>(),
                Mock.Of<ILocalGitClient>(),
                Mock.Of<ILocalGitRepoFactory>(),
                Mock.Of<IVersionDetailsParser>(),
                Mock.Of<IFileSystem>(),
                commentCollector,
                NullLogger<VmrCodeFlower>.Instance)
        {
        }

        public Task CommentIncludedPRsAsync(
            ILocalGitRepo repo,
            string lastCommit,
            string currentCommit,
            string repoUri,
            string commentHeader,
            string? pathFilter,
            CancellationToken cancellationToken)
            => CommentIncludedPRs(repo, lastCommit, currentCommit, repoUri, commentHeader, pathFilter, cancellationToken);

        protected override Task<CodeFlowResult> SameDirectionFlowAsync(
            CodeflowOptions codeflowOptions,
            LastFlows lastFlows,
            ILocalGitRepo repo,
            bool headBranchExisted,
            CancellationToken cancellationToken)
            => throw new NotImplementedException();

        protected override Task<CodeFlowResult> OppositeDirectionFlowAsync(
            CodeflowOptions codeflowOptions,
            LastFlows lastFlows,
            ILocalGitRepo sourceRepo,
            bool headBranchExisted,
            CancellationToken cancellationToken)
            => throw new NotImplementedException();

        protected override string ToSourceRepoPath(string targetPath, SourceMapping mapping)
            => throw new NotImplementedException();

        protected override bool ShouldSkipRevertCheck(string targetPath, SourceMapping mapping)
            => throw new NotImplementedException();

        protected override Task<Codeflow?> DetectCrossingFlow(
            Codeflow lastFlow,
            Backflow? lastBackFlow,
            ForwardFlow lastForwardFlow,
            ILocalGitRepo repo)
            => throw new NotImplementedException();

        protected override Task<(Codeflow, LastFlows)> UnwindPreviousFlowAsync(
            SourceMapping mapping,
            ILocalGitRepo targetRepo,
            LastFlows previousFlows,
            string branchToCreate,
            string targetBranch,
            bool unsafeFlow,
            CancellationToken cancellationToken)
            => throw new NotImplementedException();

        protected override Task EnsureCodeflowLinearityAsync(ILocalGitRepo repo, Codeflow currentFlow, LastFlows lastFlows)
            => throw new NotImplementedException();

        protected override NativePath GetEngCommonPath(NativePath sourceRepo)
            => throw new NotImplementedException();

        protected override bool TargetRepoIsVmr()
            => throw new NotImplementedException();
    }
}
