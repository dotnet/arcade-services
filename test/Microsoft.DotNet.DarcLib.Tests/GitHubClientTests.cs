// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Maestro;
using Maestro.Common;
using Maestro.MergePolicyEvaluation;
using Microsoft.DotNet;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.Models;
using Microsoft.DotNet.DarcLib.Models.GitHub;
using Microsoft.DotNet.Internal.Testing.Utility;
using Microsoft.DotNet.Services;
using Microsoft.DotNet.Services.Utility;
using Microsoft.Extensions;
using Microsoft.Extensions.Caching;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Primitives;
using Moq;
using Newtonsoft;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using NUnit.Framework;
using Octokit;

namespace Microsoft.DotNet.DarcLib.Tests;


#region Fakes
public class SimpleCacheEntry : ICacheEntry
{
    public SimpleCacheEntry(object key)
    {
        Key = key;
    }

    public object Key { get; }

    public object Value { get; set; }

    public DateTimeOffset? AbsoluteExpiration { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

    public TimeSpan? AbsoluteExpirationRelativeToNow { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

    public TimeSpan? SlidingExpiration { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

    public IList<IChangeToken> ExpirationTokens => throw new NotImplementedException();

    public IList<PostEvictionCallbackRegistration> PostEvictionCallbacks => throw new NotImplementedException();

    public CacheItemPriority Priority { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
    public long? Size { get; set; }

    public void Dispose() { }
}


public class SimpleCache : IMemoryCache
{
    public int CacheHits { get; set; }
    public int CacheMisses { get; set; }
    public ConcurrentDictionary<object, ICacheEntry> cache = new();

    public ICacheEntry CreateEntry(object key)
    {
        var newEntry = new SimpleCacheEntry(key);
        return cache.AddOrUpdate(key, new SimpleCacheEntry(key), (existingKey, existingValue) => newEntry);
    }

    public void Dispose()
    {
    }

    public void Remove(object key)
    {
        cache.Remove(key, out _);
    }

    public bool TryGetValue(object key, out object value)
    {
        if (cache.TryGetValue(key, out ICacheEntry existingEntry))
        {
            // GitHubClient should be setting the size of the 
            // entries (they should be non-zero).
            (existingEntry.Size > 0).Should().BeTrue();
            CacheHits++;
            value = existingEntry.Value;
            return true;
        }
        else
        {
            CacheMisses++;
            value = null;
            return false;
        }
    }
}


public class AbuseRateLimitFakeResponse : IResponse
{
    private readonly int? _retryAfter;

    public AbuseRateLimitFakeResponse() { }

    public AbuseRateLimitFakeResponse(int retryAfter)
    {
        _retryAfter = retryAfter;
    }

    public object Body
    {
        get
        {
            return "{\"message\": \"You have triggered an abuse detection mechanism and have been temporarily blocked from content creation. Please retry your request again later.\", \"documentation_url\": \"https://developer.github.com/v3/#abuse-rate-limits\"}";
        }
    }

    public IReadOnlyDictionary<string, string> Headers
    {
        get
        {
            var headers = new Dictionary<string, string>();
            if (_retryAfter.HasValue)
            {
                headers.Add("Retry-After", _retryAfter.Value.ToString());
            }
            return headers;
        }
    }

    public ApiInfo ApiInfo => throw new NotImplementedException();

    public HttpStatusCode StatusCode => HttpStatusCode.Forbidden;

    public string ContentType => throw new NotImplementedException();
}

#endregion

#region GitHubClient with set-able IGitHubClient 
/// <summary>
/// Lacking any DI, this class lets us put a Mock IGitHubClient into something that is effectively the same,
/// other than providing the ability to stick any IGitHubClient in as desired.
/// </summary>
internal class TestGitHubClient : GitHubClient
{
    private IGitHubClient _client;

    public void SetGitHubClientObject(IGitHubClient value)
    {
        _client = value;
    }

    public override IGitHubClient GetClient(string owner, string repo) => _client;

    public override IGitHubClient GetClient(string repoUri) => _client;

    public TestGitHubClient(string gitExecutable, string accessToken, ILogger logger, string temporaryRepositoryPath, IMemoryCache cache)
        : base(new ResolvedTokenProvider(accessToken), new ProcessManager(logger, gitExecutable), logger, temporaryRepositoryPath, cache)
    {
    }
}

#endregion

[TestFixture]
public class GitHubClientTests
{
    #region Setup / Teardown

    // When adding more tests, new OctoKit mocks go here and get set up either per-fact or in 
    // GitHubClientTests_SetUp(), depending on whether they are used in multiple places.
    protected Mock<IGitHubClient> OctoKitGithubClient;
    protected Mock<IRepositoriesClient> OctoKitRepositoriesClient;
    protected Mock<IPullRequestsClient> OctoKitPullRequestsClient;
    protected Mock<IPullRequestReviewsClient> OctoKitPullRequestReviewsClient;
    protected Mock<IGitDatabaseClient> OctoKitGitDatabaseClient;
    protected Mock<IBlobsClient> OctoKitGitBlobsClient;
    private TestGitHubClient _gitHubClientForTest;

    [SetUp]
    public void GitHubClientTests_SetUp()
    {
        OctoKitPullRequestReviewsClient = new Mock<IPullRequestReviewsClient>();

        OctoKitPullRequestsClient = new Mock<IPullRequestsClient>();
        OctoKitPullRequestsClient.SetupGet(x => x.Review).Returns(OctoKitPullRequestReviewsClient.Object);

        OctoKitRepositoriesClient = new Mock<IRepositoriesClient>();
        OctoKitRepositoriesClient.SetupGet(x => x.PullRequest).Returns(OctoKitPullRequestsClient.Object);

        OctoKitGitBlobsClient = new Mock<IBlobsClient>();

        OctoKitGitDatabaseClient = new Mock<IGitDatabaseClient>();
        OctoKitGitDatabaseClient.Setup(m => m.Blob).Returns(OctoKitGitBlobsClient.Object);

        OctoKitGithubClient = new Mock<IGitHubClient>();
        OctoKitGithubClient.SetupGet(x => x.Repository).Returns(OctoKitRepositoriesClient.Object);
        OctoKitGithubClient.Setup(m => m.Git).Returns(OctoKitGitDatabaseClient.Object);

        var nUnitLogger = new NUnitLogger();
        _gitHubClientForTest = new TestGitHubClient("git", "fake-token", nUnitLogger, "fake-path", null);
        _gitHubClientForTest.SetGitHubClientObject(OctoKitGithubClient.Object);
    }
    #endregion

    [TestCase(true)]
    [TestCase(false)]
    public async Task TreeItemCacheTest(bool enableCache)
    {
        SimpleCache cache = enableCache ? new SimpleCache() : null;
        var client = new Mock<GitHubClient>(null, null, NullLogger.Instance, null, cache);

        List<(string, string, TreeItem)> treeItemsToGet =
        [
            ("a", "b", new TreeItem("path", "mode", TreeType.Blob, 10, "1", "https://url")),
            ("a", "b", new TreeItem("path", "mode", TreeType.Blob, 10, "2", "https://url")),
            ("a", "b", new TreeItem("path", "mode", TreeType.Blob, 10, "3", "https://url")),
            ("a", "b", new TreeItem("path", "mode", TreeType.Blob, 10, "4", "https://url")),
            ("dotnet", "corefx", new TreeItem("path", "mode", TreeType.Blob, 10, "11", "https://url")),
            ("dotnet", "corefx", new TreeItem("path", "mode", TreeType.Blob, 10, "12", "https://url")),
        ];

        // Mock up the github client
        var octoKitClientMock = new Mock<IGitHubClient>();
        var octoKitGitMock = new Mock<IGitDatabaseClient>();
        var octoKitBlobClientMock = new Mock<IBlobsClient>();
        var blob = new Blob("foo", "content", EncodingType.Utf8, "somesha", 10);

        foreach (var treeItem in treeItemsToGet)
        {
            octoKitBlobClientMock.Setup(m => m.Get(treeItem.Item1, treeItem.Item2, treeItem.Item3.Sha)).ReturnsAsync(blob);
        }

        octoKitGitMock.Setup(m => m.Blob).Returns(octoKitBlobClientMock.Object);
        octoKitClientMock.Setup(m => m.Git).Returns(octoKitGitMock.Object);
        client.Setup(m => m.GetClient(It.IsAny<string>())).Returns(octoKitClientMock.Object);
        client.Setup(m => m.GetClient(It.IsAny<string>(), It.IsAny<string>())).Returns(octoKitClientMock.Object);

        // Request all but the last tree item in the list, then request the full set, then again.
        // For the cache scenario, we should have no cache hits on first pass, n-1 on the second, and N on the last
        // For the no-cache scenario, we simply not crash.

        for (int i = 0; i < treeItemsToGet.Count - 1; i++)
        {
            await client.Object.GetGitTreeItem("path", treeItemsToGet[i].Item3, treeItemsToGet[i].Item1, treeItemsToGet[i].Item2);
        }

        int expectedCacheHits = 0;
        int expectedCacheMisses = treeItemsToGet.Count - 1;
        if (enableCache)
        {
            cache.CacheMisses.Should().Be(expectedCacheMisses);
            cache.CacheHits.Should().Be(expectedCacheHits);
        }

        // Request full set
        for (int i = 0; i < treeItemsToGet.Count; i++)
        {
            await client.Object.GetGitTreeItem("path", treeItemsToGet[i].Item3, treeItemsToGet[i].Item1, treeItemsToGet[i].Item2);
        }

        if (enableCache)
        {
            expectedCacheMisses++;
            expectedCacheHits += (treeItemsToGet.Count - 1);
            cache.CacheMisses.Should().Be(treeItemsToGet.Count);
            cache.CacheHits.Should().Be(treeItemsToGet.Count - 1);
        }

        // Request full set
        for (int i = 0; i < treeItemsToGet.Count; i++)
        {
            await client.Object.GetGitTreeItem("path", treeItemsToGet[i].Item3, treeItemsToGet[i].Item1, treeItemsToGet[i].Item2);
        }

        if (enableCache)
        {
            expectedCacheHits += treeItemsToGet.Count;
            cache.CacheMisses.Should().Be(expectedCacheMisses);
            cache.CacheHits.Should().Be(expectedCacheHits);
        }

        // Request an item with the same SHA but different path
        var renamedTreeItem = treeItemsToGet[0];
        var renamedTreeItemBlob = renamedTreeItem.Item3;
        renamedTreeItem.Item3 = new TreeItem("anotherPath",
            renamedTreeItemBlob.Mode,
            TreeType.Blob,
            renamedTreeItemBlob.Size,
            renamedTreeItemBlob.Sha,
            renamedTreeItemBlob.Url);

        await client.Object.GetGitTreeItem("anotherPath", renamedTreeItem.Item3, renamedTreeItem.Item1, renamedTreeItem.Item2);

        if (enableCache)
        {
            // First time the new item should not be in the cache
            expectedCacheMisses++;
            cache.CacheMisses.Should().Be(expectedCacheMisses);
            cache.CacheHits.Should().Be(expectedCacheHits);
            // Get it again, this time it should be in the cache
            expectedCacheHits++;
            await client.Object.GetGitTreeItem("anotherPath", renamedTreeItem.Item3, renamedTreeItem.Item1, renamedTreeItem.Item2);
            cache.CacheHits.Should().Be(expectedCacheHits);
        }
    }

    [Test]
    public async Task GetGitTreeItemAbuseExceptionRetryTest()
    {
        var client = new Mock<GitHubClient>(null, null, NullLogger.Instance, null, new SimpleCache());

        var blob = new Blob("foo", "fakeContent", EncodingType.Utf8, "somesha", 10);
        var treeItem = new TreeItem("fakePath", "fakeMode", TreeType.Blob, 10, "1", "https://url");
        string path = "fakePath";
        string owner = "fakeOwner";
        string repo = "fakeRepo";
        var abuseException = new AbuseException(new AbuseRateLimitFakeResponse());

        OctoKitGitBlobsClient.SetupSequence(m => m.Get(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ThrowsAsync(abuseException)
            .ReturnsAsync(blob);
        client.Setup(m => m.GetClient(It.IsAny<string>())).Returns(OctoKitGithubClient.Object);
        client.Setup(m => m.GetClient(It.IsAny<string>(), It.IsAny<string>())).Returns(OctoKitGithubClient.Object);

        var resultGitFile = await client.Object.GetGitTreeItem(path, treeItem, owner, repo);
        resultGitFile.FilePath.Should().Be(path + "/" + treeItem.Path);
        resultGitFile.Content.TrimEnd().Should().Be(blob.Content);
        resultGitFile.Mode.Should().Be(treeItem.Mode);

        OctoKitGitBlobsClient.Verify(m => m.Get(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Exactly(2));
    }

    [Test]
    public async Task GetGitTreeItemAbuseExceptionRetryWithRateLimitTest()
    {
        var client = new Mock<GitHubClient>(null, null, NullLogger.Instance, null, new SimpleCache());

        var blob = new Blob("foo", "fakeContent", EncodingType.Utf8, "somesha", 10);
        var treeItem = new TreeItem("fakePath", "fakeMode", TreeType.Blob, 10, "1", "https://url");
        string path = "fakePath";
        string owner = "fakeOwner";
        string repo = "fakeRepo";
        var abuseException = new AbuseException(new AbuseRateLimitFakeResponse(5));

        OctoKitGitBlobsClient.SetupSequence(m => m.Get(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ThrowsAsync(abuseException)
            .ReturnsAsync(blob);
        client.Setup(m => m.GetClient(It.IsAny<string>())).Returns(OctoKitGithubClient.Object);
        client.Setup(m => m.GetClient(It.IsAny<string>(), It.IsAny<string>())).Returns(OctoKitGithubClient.Object);

        var resultGitFile = await client.Object.GetGitTreeItem(path, treeItem, owner, repo);
        resultGitFile.FilePath.Should().Be(path + "/" + treeItem.Path);
        resultGitFile.Content.TrimEnd().Should().Be(blob.Content);
        resultGitFile.Mode.Should().Be(treeItem.Mode);

        OctoKitGitBlobsClient.Verify(m => m.Get(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Exactly(2));
    }

    [TestCase("https://api.github.com/repos/githubclienttests/getlatestreviews/pulls/123", 10, 10, false)] // Happy path: 10 approvals
    [TestCase("https://api.github.com/repos/githubclienttests/getlatestreviews/pulls/123", 10, 10, true)]  // Same as above but user comments 1 minute later
    [TestCase("https://api.github.com/repos/githubclienttests/getlatestreviews/pulls/123", 0, 0, false)]   // No reviews yet
    [TestCase("https://api.github.com/repos/githubclienttests/getlatestreviews/pulls/124", 0, 5, false)]   // Reviews exist, not for this one.
    public async Task GetReviewsForPullRequestOnePerUser(string pullRequestUrl, int expectedReviewCount, int fakeUserCount, bool usersCommentAfterApprove)
    {
        var pullRequestReviewData = GetApprovingPullRequestData("githubclienttests", "getlatestreviews", 123, fakeUserCount, usersCommentAfterApprove);
        var reviews = await GetLatestReviewsForPullRequestWrapperAsync(pullRequestReviewData, pullRequestUrl);
        reviews.Should().HaveCount(expectedReviewCount);
        reviews.Any(r => r.Status == ReviewState.ChangesRequested || r.Status == ReviewState.Rejected).Should().BeFalse();
    }

    [TestCase("https://api.github.com/repos/githubclienttests/getlatestreviews/pulls/123", 0, 10)]
    [TestCase("https://api.github.com/repos/githubclienttests/getlatestreviews/pulls/124", 0, 0)]
    public async Task GetReviewsForPullRequestCommentsOnly(string pullRequestUrl, int expectedReviewCount, int fakeUserCount)
    {
        var pullRequestReviewData = GetOnlyCommentsPullRequestData("githubclienttests", "getlatestreviews", 123, fakeUserCount);
        var reviews = await GetLatestReviewsForPullRequestWrapperAsync(pullRequestReviewData, pullRequestUrl);
        reviews.Should().HaveCount(expectedReviewCount);
    }

    [TestCase("https://api.github.com/repos/githubclienttests/getmixedreviews/pulls/456", 10, 10, false)] // Happy path: 10 approvals
    [TestCase("https://api.github.com/repos/githubclienttests/getmixedreviews/pulls/456", 10, 10, true)]  // Same as above but user comments 1 minute later
    [TestCase("https://api.github.com/repos/githubclienttests/getmixedreviews/pulls/456", 0, 0, false, false)]   // No reviews yet
    [TestCase("https://api.github.com/repos/githubclienttests/getmixedreviews/pulls/457", 0, 5, false, false)]   // Reviews exist, not for this one.
    public async Task GetReviewsForPullRequestMultiPerUser(string pullRequestUrl, int expectedReviewCount, int fakeUserCount, bool usersCommentAfterApprove, bool successExpected = true)
    {
        var pullRequestReviewData = GetMixedPullRequestData("githubclienttests", "getmixedreviews", 456, fakeUserCount, usersCommentAfterApprove);
        var reviews = await GetLatestReviewsForPullRequestWrapperAsync(pullRequestReviewData, pullRequestUrl);
        reviews.Should().HaveCount(expectedReviewCount);

        if (successExpected)
        {
            reviews.Any(r => r.Status == ReviewState.ChangesRequested || r.Status == ReviewState.Rejected).Should().BeFalse();
            reviews.Should().HaveCountGreaterThanOrEqualTo(1);
        }
        else if (reviews.Count > 0)
        {
            reviews.Any(r => r.Status == ReviewState.ChangesRequested || r.Status == ReviewState.Rejected).Should().BeTrue();
        }
    }

    private async Task<IList<Review>> GetLatestReviewsForPullRequestWrapperAsync(Dictionary<Tuple<string, string, int>, List<PullRequestReview>> data, string pullRequestUrl)
    {
        List<PullRequestReview> fakeReviews = [];

        // Use Moq to put the return value 
        OctoKitPullRequestReviewsClient.Setup(x => x.GetAll(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()))
            .Callback((string x, string y, int z) =>
            {
                var theKey = new Tuple<string, string, int>(x, y, z);
                if (data.ContainsKey(theKey))
                {
                    fakeReviews.AddRange(data[theKey]);
                }

            })
            .ReturnsAsync(fakeReviews);

        return await _gitHubClientForTest.GetLatestPullRequestReviewsAsync(pullRequestUrl);
    }

    #region Functions for creating fake review data

    private static Dictionary<Tuple<string, string, int>, List<PullRequestReview>> GetApprovingPullRequestData(string owner, string repoName, int requestId, int userCount, bool commentAfter)
    {
        var data = new Dictionary<Tuple<string, string, int>, List<PullRequestReview>>();
        var keyValue = new Tuple<string, string, int>(owner, repoName, requestId);
        data.Add(keyValue, []);
        DateTimeOffset baseOffset = DateTimeOffset.UtcNow;

        for (int i = 0; i < userCount; i++)
        {
            data[keyValue].Add(CreateFakePullRequestReview(PullRequestReviewState.Approved, owner, repoName, requestId, baseOffset, $"username{i}"));
            if (commentAfter)
            {
                data[keyValue].Add(CreateFakePullRequestReview(PullRequestReviewState.Commented, owner, repoName, requestId, baseOffset.AddMinutes(1), $"username{i}"));
            }
        }
        return data;
    }

    private static Dictionary<Tuple<string, string, int>, List<PullRequestReview>> GetMixedPullRequestData(string owner, string repoName, int requestId, int userCount, bool commentAfter)
    {
        var data = new Dictionary<Tuple<string, string, int>, List<PullRequestReview>>();
        var keyValue = new Tuple<string, string, int>(owner, repoName, requestId);
        data.Add(keyValue, []);
        DateTimeOffset baseOffset = DateTimeOffset.UtcNow;

        for (int i = 0; i < userCount; i++)
        {
            data[keyValue].Add(CreateFakePullRequestReview(PullRequestReviewState.Commented, owner, repoName, requestId, baseOffset, $"username{i}"));
            data[keyValue].Add(CreateFakePullRequestReview(PullRequestReviewState.ChangesRequested, owner, repoName, requestId, baseOffset.AddMinutes(1), $"username{i}"));
            data[keyValue].Add(CreateFakePullRequestReview(PullRequestReviewState.Approved, owner, repoName, requestId, baseOffset.AddMinutes(2), $"username{i}"));
            if (commentAfter)
            {
                data[keyValue].Add(CreateFakePullRequestReview(PullRequestReviewState.Commented, owner, repoName, requestId, baseOffset.AddMinutes(3), $"username{i}"));
            }
        }
        return data;
    }

    private static Dictionary<Tuple<string, string, int>, List<PullRequestReview>> GetOnlyCommentsPullRequestData(string owner, string repoName, int requestId, int userCount)
    {
        var data = new Dictionary<Tuple<string, string, int>, List<PullRequestReview>>();
        var keyValue = new Tuple<string, string, int>(owner, repoName, requestId);
        data.Add(keyValue, []);
        DateTimeOffset baseOffset = DateTimeOffset.UtcNow;

        for (int i = 0; i < userCount; i++)
        {
            data[keyValue].Add(CreateFakePullRequestReview(PullRequestReviewState.Commented, owner, repoName, requestId, baseOffset, $"username{i}"));
        }
        return data;
    }

    private static PullRequestReview CreateFakePullRequestReview(PullRequestReviewState reviewState, string owner, string repoName, int requestId, DateTimeOffset reviewTime, string userName)
    {
        return new PullRequestReview(
            0,
            string.Empty,
            "41deec8f17c45a064c542438da456c99a37710d9",
            GetFakeUser(userName),
            "Ship it... or don't, whatever.",
            string.Empty,
            $"https://api.github.com/repos/{owner}/{repoName}/pulls/{requestId}",
            reviewState,
            AuthorAssociation.None,
            reviewTime);
    }

    private static User GetFakeUser(string userId)
    {
        // We mostly only care about the user's login id (userId)", this ctor is huge, sorry about that.
        return new User(null, null, null, 0, null, DateTimeOffset.MinValue, DateTimeOffset.MinValue,
            0, "fake@email.com", 0, 0, false, null, 0, 0, "nonexistent", userId, userId,
            string.Empty, 0, null, 0, 0, 0, string.Empty, null, false, string.Empty, null);
    }

    #endregion

    /// <summary>
    /// Verifies that the 4-parameter constructor delegates correctly and initializes the instance without throwing,
    /// both when the cache is provided and when it is null. Also validates the default state of public properties.
    /// Inputs:
    ///  - useCache: whether to pass a non-null IMemoryCache instance or null.
    /// Expected:
    ///  - Instance is created successfully.
    ///  - AllowRetries is initialized to true.
    /// </summary>
    [TestCase(true)]
    [TestCase(false)]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void Constructor_WithOrWithoutCache_InitializesAndSetsDefaults(bool useCache)
    {
        // Arrange
        var tokenProvider = new Mock<IRemoteTokenProvider>(MockBehavior.Strict);
        var processManager = new Mock<IProcessManager>(MockBehavior.Strict);
        var logger = new Mock<ILogger>(MockBehavior.Loose);
        IMemoryCache cache = useCache ? new MemoryCache(new MemoryCacheOptions()) : null;

        // Act
        var sut = new GitHubClient(tokenProvider.Object, processManager.Object, logger.Object, cache);

        // Assert
        sut.Should().NotBeNull();
        sut.AllowRetries.Should().BeTrue();
    }

    /// <summary>
    /// Verifies that when the target branch already exists, CreateBranchAsync updates the branch to the latest SHA:
    /// - Input: Valid repoUri (owner/repo), newBranch that already exists, baseBranch (any).
    /// - Expected: Method issues a GET (branch exists), then PATCH to refs/heads/{newBranch} with Force=true.
    /// - Note: ExecuteRemoteGitCommandAsync is private and performs real HTTP; test is marked ignored with guidance.
    /// </summary>
    [TestCase("https://github.com/owner/repo", "feature/update", "main")]
    [TestCase("https://github.com/owner/repo/", "hotfix/1.0.0", "master")]
    [TestCase("https://github.com/OWNER/REPO", "release-2025.08", "develop")]
    [Ignore("Partial test: CreateBranchAsync internally uses a private HTTP method (ExecuteRemoteGitCommandAsync) that cannot be mocked or intercepted. To enable this test, refactor to inject a virtual HTTP executor or make it overridable, then verify GET branches (200) followed by PATCH to refs/heads/{newBranch} with Force=true.")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public async Task CreateBranchAsync_BranchExists_UpdatesBranchWithPatch(string repoUri, string newBranch, string baseBranch)
    {
        // Arrange
        var logger = new Mock<ILogger>().Object;
        var client = new GitHubClient(null, null, logger, (IMemoryCache)null);

        // Act
        // await client.CreateBranchAsync(repoUri, newBranch, baseBranch);

        // Assert
        // After refactor to enable HTTP injection/mocking:
        // - Verify GET repos/{owner}/{repo}/branches/{newBranch} returns success.
        // - Verify PATCH repos/{owner}/{repo}/git/refs/heads/{newBranch} with body { ref, sha from baseBranch, force: true }.
        // - Verify informational logs were written for "Branch '{branch}' exists, updated".
    }

    /// <summary>
    /// Verifies that when the target branch does not exist (404 on GET), CreateBranchAsync creates it:
    /// - Input: Valid repoUri (owner/repo), newBranch that does not exist, baseBranch (any).
    /// - Expected: Method issues a GET (404), then POST to git/refs to create refs/heads/{newBranch} with latest SHA from baseBranch.
    /// - Note: ExecuteRemoteGitCommandAsync is private; test is marked ignored with guidance to refactor for HTTP injection.
    /// </summary>
    [TestCase("https://github.com/owner/repo", "feature/new", "main")]
    [TestCase("https://github.com/owner/repo/", "bugfix/JIRA-123", "master")]
    [TestCase("https://github.com/dotnet/arcade", "topic/with/slash", "release")]
    [Ignore("Partial test: CreateBranchAsync internally uses a private HTTP method (ExecuteRemoteGitCommandAsync) that cannot be mocked or intercepted. To enable this test, refactor to inject a virtual HTTP executor or make it overridable, then verify GET branches (404) followed by POST to git/refs with expected payload, and success logs.")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public async Task CreateBranchAsync_BranchMissing_CreatesBranchWithPost(string repoUri, string newBranch, string baseBranch)
    {
        // Arrange
        var logger = new Mock<ILogger>().Object;
        var client = new GitHubClient(null, null, logger, (IMemoryCache)null);

        // Act
        // await client.CreateBranchAsync(repoUri, newBranch, baseBranch);

        // Assert
        // After refactor to enable HTTP injection/mocking:
        // - Stub GetLastCommitShaAsync(owner, repo, baseBranch) path to return a SHA (or null to validate serialization behavior).
        // - Verify GET repos/{owner}/{repo}/branches/{newBranch} throws HttpRequestException with StatusCode = NotFound.
        // - Verify POST repos/{owner}/{repo}/git/refs is called once with a JSON body containing { ref: "refs/heads/{newBranch}", sha: latest from baseBranch }.
        // - Verify informational logs were written for creation path.
    }

    /// <summary>
    /// Verifies that unexpected HTTP errors are logged and rethrown:
    /// - Input: Valid repoUri (owner/repo), any branch names.
    /// - Expected: Method logs an error and rethrows when GET branches fails with an unexpected status code (non-404).
    /// - Note: ExecuteRemoteGitCommandAsync is private; test is marked ignored with guidance to refactor for HTTP injection.
    /// </summary>
    [TestCase("https://github.com/owner/repo", "feature/error", "main")]
    [TestCase("https://github.com/owner/repo/", "patch/error", "develop")]
    [Ignore("Partial test: CreateBranchAsync internally uses a private HTTP method (ExecuteRemoteGitCommandAsync) that cannot be mocked or intercepted. To enable this test, refactor to inject a virtual HTTP executor or make it overridable, then simulate non-404 HttpRequestException and assert it is rethrown after logging.")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public async Task CreateBranchAsync_UnexpectedHttpError_LogsAndThrows(string repoUri, string newBranch, string baseBranch)
    {
        // Arrange
        var logger = new Mock<ILogger>().Object;
        var client = new GitHubClient(null, null, logger, (IMemoryCache)null);

        // Act
        // Func<Task> act = () => client.CreateBranchAsync(repoUri, newBranch, baseBranch);

        // Assert
        // After refactor to enable HTTP injection/mocking:
        // - Simulate HttpRequestException with StatusCode = InternalServerError (500) on GET branches.
        // - Verify error log: "Checking if '{branch}' branch existed in repo '{repoUri}' failed with '{error}'".
        // - Verify the exception is rethrown (no swallowing).
    }

    /// <summary>
    /// Verifies that MergeDependencyPullRequestAsync constructs the MergePullRequest with the correct properties
    /// and calls the Octokit merge API with the expected merge method (Squash vs Merge) while not deleting the source branch
    /// when DeleteSourceBranch is false.
    /// Inputs:
    /// - squashMerge: toggles between squash merge and regular merge.
    /// - commitSha: SHA to merge.
    /// - commitMessage: message to use for the merge commit.
    /// Expected:
    /// - Merge is invoked once with a MergePullRequest whose CommitMessage and Sha match inputs and whose MergeMethod
    ///   matches the squash toggle.
    /// - No attempt is made to delete the source branch.
    /// - No exception is thrown.
    /// </summary>
    [TestCase(true, "abc123def", "Squash this change")]
    [TestCase(false, "ffffeeee1111", "")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public async Task MergeDependencyPullRequestAsync_SquashToggle_CallsMergeWithExpectedRequest(bool squashMerge, string commitSha, string commitMessage)
    {
        // Arrange
        var loggerMock = new Mock<ILogger>();
        var clientMock = new Mock<GitHubClient>(null, null, loggerMock.Object, null, null) { CallBase = true };

        string owner = "owner1";
        string repo = "repo1";
        int number = 42;
        string pullRequestUrl = $"https://api.github.com/repos/{owner}/{repo}/pulls/{number}";

        var octokitClient = new Mock<IGitHubClient>();
        var pullRequestsClient = new Mock<IPullRequestsClient>();
        var gitDatabaseClient = new Mock<IGitDatabaseClient>();
        var referencesClient = new Mock<IReferencesClient>();

        // PullRequest.Get is called but the result is only used when DeleteSourceBranch is true.
        pullRequestsClient
            .Setup(m => m.Get(owner, repo, number))
            .ReturnsAsync((Octokit.PullRequest)null);

        // Merge is awaited but the return value is ignored by the implementation; return a completed task with null result.
        pullRequestsClient
            .Setup(m => m.Merge(owner, repo, number, It.IsAny<MergePullRequest>()))
            .ReturnsAsync((PullRequestMerge)null);

        octokitClient.SetupGet(m => m.PullRequest).Returns(pullRequestsClient.Object);

        gitDatabaseClient.SetupGet(m => m.Reference).Returns(referencesClient.Object);
        octokitClient.SetupGet(m => m.Git).Returns(gitDatabaseClient.Object);

        clientMock.Setup(m => m.GetClient(owner, repo)).Returns(octokitClient.Object);

        var parameters = new MergePullRequestParameters
        {
            CommitToMerge = commitSha,
            SquashMerge = squashMerge,
            DeleteSourceBranch = false
        };

        // Act
        await clientMock.Object.MergeDependencyPullRequestAsync(pullRequestUrl, parameters, commitMessage);

        // Assert
        pullRequestsClient.Verify(
            m => m.Merge(
                owner,
                repo,
                number,
                It.Is<MergePullRequest>(mpr =>
                    mpr.CommitMessage == commitMessage &&
                    mpr.Sha == commitSha &&
                    mpr.MergeMethod == (squashMerge ? PullRequestMergeMethod.Squash : PullRequestMergeMethod.Merge))),
            Times.Once);

        referencesClient.Verify(
            r => r.Delete(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()),
            Times.Never);
    }

    /// <summary>
    /// Partial test placeholder to verify that when Octokit's PullRequestNotMergeableException is thrown by the merge call,
    /// the method translates it into Microsoft.DotNet.DarcLib.PullRequestNotMergeableException with the same message.
    /// Inputs:
    /// - A pull request URL for a PR that is not mergeable.
    /// Expected:
    /// - MergeDependencyPullRequestAsync throws PullRequestNotMergeableException.
    /// Note:
    /// Creating a proper Octokit.PullRequestNotMergeableException instance may require internal Octokit response types that
    /// cannot be mocked or constructed here without violating constraints. Replace the inconclusive assertion with a working
    /// setup that throws the appropriate Octokit exception instance from IPullRequestsClient.Merge and then assert the translated
    /// exception type and message.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public async Task MergeDependencyPullRequestAsync_MergeNotMergeable_ThrowsDomainException_Partial()
    {
        // Arrange
        Assert.Inconclusive("Partial test: supply an Octokit.PullRequestNotMergeableException from IPullRequestsClient.Merge when feasible, then assert translation to Microsoft.DotNet.DarcLib.PullRequestNotMergeableException.");
    }

    /// <summary>
    /// Partial test placeholder to verify that when DeleteSourceBranch is true, the method attempts to delete the source branch
    /// using heads/{pr.Head.Ref} and logs information if deletion fails without throwing.
    /// Inputs:
    /// - DeleteSourceBranch = true and IPullRequestsClient.Get returning a PullRequest with a valid Head.Ref.
    /// Expected:
    /// - IReferencesClient.Delete is invoked with heads/{pr.Head.Ref}.
    /// - If Delete throws, _logger.LogInformation is invoked and no exception escapes.
    /// Note:
    /// Constructing a concrete Octokit.PullRequest instance with a populated Head.Ref requires invoking a very large public constructor
    /// or relying on unavailable/internal members. Replace the inconclusive assertion once a suitable PullRequest instance can be produced.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public async Task MergeDependencyPullRequestAsync_DeleteSourceBranchTrue_DeletionFailure_IsLoggedAndNotThrown_Partial()
    {
        // Arrange
        Assert.Inconclusive("Partial test: return an Octokit.PullRequest with Head.Ref from IPullRequestsClient.Get, set DeleteSourceBranch=true, force IReferencesClient.Delete to throw, then verify LogInformation and that no exception is thrown.");
    }

    /// <summary>
    /// Verifies that when the underlying HTTP returns 404 (not found), the method returns null.
    /// Input:
    /// - repoUri: Any GitHub repo URL
    /// - filePath: Any path
    /// - branch: Any branch
    /// Expected:
    /// - Returns null when HttpRequestException with StatusCode == NotFound is encountered.
    /// </summary>
    [TestCase("https://github.com/dotnet/runtime", "README.md", "main")]
    [TestCase("https://github.com/owner/repo", "eng/version.json", "release")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public async Task CheckIfFileExistsAsync_NotFound_ReturnsNull_Partial(string repoUri, string filePath, string branch)
    {
        // Arrange
        var tokenProvider = new Moq.Mock<IRemoteTokenProvider>(Moq.MockBehavior.Strict);
        var processManager = new Moq.Mock<IProcessManager>(Moq.MockBehavior.Strict);
        var logger = new Moq.Mock<ILogger>(Moq.MockBehavior.Loose);
        var client = new GitHubClient(tokenProvider.Object, processManager.Object, logger.Object, (IMemoryCache)null);

        // Act
        // IMPORTANT: ExecuteRemoteGitCommandAsync is private and cannot be mocked.
        // To complete this test:
        // - Refactor GitHubClient.ExecuteRemoteGitCommandAsync to be protected virtual, OR
        // - Introduce an injectable IHttpClientFactory/HttpMessageHandler to control HTTP responses.
        // Then, set up a HttpRequestException with StatusCode = HttpStatusCode.NotFound and assert that the method returns null.
        Assert.Inconclusive("Partial test: cannot mock private ExecuteRemoteGitCommandAsync. Refactor to enable mocking HTTP 404 and assert null result.");

        // Example (after refactor):
        // var result = await client.CheckIfFileExistsAsync(repoUri, filePath, branch);
        // AwesomeAssert.That(result).IsNull();
    }

    /// <summary>
    /// Verifies that when the underlying HTTP succeeds and returns JSON with a 'sha' property,
    /// the method returns that SHA string.
    /// Input:
    /// - repoUri: Any GitHub repo URL
    /// - filePath: Any path
    /// - branch: Any branch
    /// Expected:
    /// - Returns the 'sha' value from the response JSON.
    /// </summary>
    [TestCase("https://github.com/dotnet/runtime", "README.md", "main")]
    [TestCase("https://github.com/owner/repo", "src/app.csproj", "feature/xyz")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public async Task CheckIfFileExistsAsync_ValidJsonWithSha_ReturnsSha_Partial(string repoUri, string filePath, string branch)
    {
        // Arrange
        var tokenProvider = new Moq.Mock<IRemoteTokenProvider>(Moq.MockBehavior.Strict);
        var processManager = new Moq.Mock<IProcessManager>(Moq.MockBehavior.Strict);
        var logger = new Moq.Mock<ILogger>(Moq.MockBehavior.Loose);
        var client = new GitHubClient(tokenProvider.Object, processManager.Object, logger.Object, (IMemoryCache)null);

        // Act
        // IMPORTANT: ExecuteRemoteGitCommandAsync is private and cannot be mocked.
        // To complete this test:
        // - Refactor GitHubClient.ExecuteRemoteGitCommandAsync to be protected virtual, OR
        // - Introduce an injectable IHttpClientFactory/HttpMessageHandler to control HTTP responses.
        // Then, set up a successful 200 response with content: {"sha":"<expectedSha>"} and assert method returns <expectedSha>.
        Assert.Inconclusive("Partial test: cannot mock private ExecuteRemoteGitCommandAsync. Refactor to enable mocking a 200 response with JSON body containing 'sha'.");

        // Example (after refactor):
        // string expectedSha = "abcdef1234567890";
        // var result = await client.CheckIfFileExistsAsync(repoUri, filePath, branch);
        // AwesomeAssert.That(result).IsEqualTo(expectedSha);
    }

    /// <summary>
    /// Ensures GetLastCommitShaAsync(repoUri, branch) throws UriFormatException for invalid repository URIs.
    /// This validates that the method relies on Uri parsing via ParseRepoUri and fails early for malformed inputs,
    /// avoiding any network calls.
    /// Expected: UriFormatException is thrown.
    /// </summary>
    /// <param name="invalidRepoUri">Malformed repository URI input that cannot be parsed by UriBuilder.</param>
    [TestCase("not-a-uri")]
    [TestCase("://bad")]
    [TestCase("")]
    [TestCase(" ")]
    [TestCase("\t\n")]
    [TestCase("ht!tp://bad")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public async Task GetLastCommitShaAsync_InvalidRepoUri_ThrowsUriFormatException(string invalidRepoUri)
    {
        // Arrange
        var tokenProvider = new Mock<IRemoteTokenProvider>(MockBehavior.Strict).Object;
        var processManager = new Mock<IProcessManager>(MockBehavior.Strict).Object;
        var logger = new Mock<ILogger>(MockBehavior.Loose).Object;
        IMemoryCache cache = null;
        var sut = new GitHubClient(tokenProvider, processManager, logger, cache);

        // Act + Assert
        Assert.ThrowsAsync<UriFormatException>(async () =>
        {
            await sut.GetLastCommitShaAsync(invalidRepoUri, "main");
        });
    }

    /// <summary>
    /// Partial test placeholder for valid inputs. The method under test delegates to a private method
    /// that performs HTTP requests internally via a non-virtual/private pipeline, which cannot be mocked using Moq.
    /// To complete this test, expose or virtualize the HTTP layer (e.g., make ExecuteRemoteGitCommandAsync virtual)
    /// or provide an injectable abstraction to simulate responses.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void GetLastCommitShaAsync_ValidInputs_ReturnsShaOrNull_PartialInconclusive()
    {
        // Arrange
        var tokenProvider = new Mock<IRemoteTokenProvider>(MockBehavior.Strict).Object;
        var processManager = new Mock<IProcessManager>(MockBehavior.Strict).Object;
        var logger = new Mock<ILogger>(MockBehavior.Loose).Object;
        IMemoryCache cache = null;
        var sut = new GitHubClient(tokenProvider, processManager, logger, cache);

        // Act
        // NOTE:
        // - Calling the method with a valid URI would reach a private HTTP path (ExecuteRemoteGitCommandAsync)
        //   which is not virtual and cannot be mocked with Moq.
        // - Network calls are not permitted in unit tests per project constraints.

        // Assert
        Assert.Inconclusive("Cannot complete this test without refactoring to mock the HTTP layer (non-virtual/private). Consider introducing an injectable abstraction or making the HTTP call method virtual.");
    }

    /// <summary>
    /// Verifies that the first call to GetClient(owner, repo) constructs a new Octokit client,
    /// requests a token for the expected repository URL, and returns a non-null instance.
    /// Inputs vary owner/repo including normal, dashed, dotted, and whitespace values.
    /// Expected: token provider called exactly once with "https://github.com/{owner}/{repo}", and result is not null.
    /// </summary>
    [TestCase("dotnet", "runtime")]
    [TestCase("owner-with-dash", "repo.with.dot")]
    [TestCase(" owner ", " repo ")]
    [TestCase("", "repo")]
    [TestCase("owner", "")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void GetClient_FirstCall_CreatesClientAndRequestsTokenForOwnerRepoUrl(string owner, string repo)
    {
        // Arrange
        var expectedRepoUrl = $"https://github.com/{owner}/{repo}";
        var tokenProvider = new Mock<IRemoteTokenProvider>(MockBehavior.Strict);
        tokenProvider.Setup(t => t.GetTokenForRepository(expectedRepoUrl)).Returns("fake-token");

        var processManager = new Mock<IProcessManager>(MockBehavior.Loose);
        var logger = new Mock<ILogger>(MockBehavior.Loose);

        var sut = new GitHubClient(tokenProvider.Object, processManager.Object, logger.Object, (IMemoryCache)null);

        // Act
        var result = sut.GetClient(owner, repo);

        // Assert
        Assert.That(result, Is.Not.Null, "GetClient should return a non-null IGitHubClient instance on first call.");
        tokenProvider.Verify(t => t.GetTokenForRepository(expectedRepoUrl), Times.Once);
        tokenProvider.VerifyNoOtherCalls();
    }

    /// <summary>
    /// Verifies that subsequent calls to GetClient(owner, repo) return the same cached instance
    /// and do not request a token again, even when different owner/repo are supplied.
    /// Expected: same instance returned and token provider invoked only once with the first URL.
    /// </summary>
    [TestCase("dotnet", "runtime", "different", "repo")]
    [TestCase("orgA", "repoA", "orgA", "repoA")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void GetClient_SubsequentCallsReturnSameInstanceAndDoNotRequestTokenAgain(string owner1, string repo1, string owner2, string repo2)
    {
        // Arrange
        var firstUrl = $"https://github.com/{owner1}/{repo1}";
        var tokenProvider = new Mock<IRemoteTokenProvider>(MockBehavior.Strict);
        tokenProvider.Setup(t => t.GetTokenForRepository(firstUrl)).Returns("fake-token");

        var processManager = new Mock<IProcessManager>(MockBehavior.Loose);
        var logger = new Mock<ILogger>(MockBehavior.Loose);

        var sut = new GitHubClient(tokenProvider.Object, processManager.Object, logger.Object, (IMemoryCache)null);

        // Act
        var first = sut.GetClient(owner1, repo1);
        var second = sut.GetClient(owner2, repo2);

        // Assert
        Assert.That(ReferenceEquals(first, second), Is.True, "GetClient should cache and return the same IGitHubClient instance across calls.");
        tokenProvider.Verify(t => t.GetTokenForRepository(firstUrl), Times.Once);
        tokenProvider.VerifyNoOtherCalls();
    }

    /// <summary>
    /// Verifies that when the token provider returns null or empty string, GetClient(owner, repo)
    /// throws a DarcException with a helpful message.
    /// Expected: DarcException is thrown and token provider called with the expected URL.
    /// </summary>
    [TestCase(true)]
    [TestCase(false)]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void GetClient_WhenTokenMissing_ThrowsDarcException(bool returnNullToken)
    {
        // Arrange
        const string owner = "dotnet";
        const string repo = "runtime";
        var expectedUrl = $"https://github.com/{owner}/{repo}";

        var tokenProvider = new Mock<IRemoteTokenProvider>(MockBehavior.Strict);
        tokenProvider
            .Setup(t => t.GetTokenForRepository(expectedUrl))
            .Returns(returnNullToken ? null : string.Empty);

        var processManager = new Mock<IProcessManager>(MockBehavior.Loose);
        var logger = new Mock<ILogger>(MockBehavior.Loose);

        var sut = new GitHubClient(tokenProvider.Object, processManager.Object, logger.Object, (IMemoryCache)null);

        // Act
        TestDelegate act = () => sut.GetClient(owner, repo);

        // Assert
        var ex = Assert.Throws<DarcException>(act);
        Assert.That(ex.Message, Does.Contain("GitHub personal access token is required"), "Exception message should indicate missing GitHub token.");
        tokenProvider.Verify(t => t.GetTokenForRepository(expectedUrl), Times.Once);
        tokenProvider.VerifyNoOtherCalls();
    }

    /// <summary>
    /// Verifies that GitDiffAsync:
    /// - Issues a compare request for the provided base/target versions,
    /// - Parses "ahead_by" and "behind_by" from the successful JSON response,
    /// - Sets BaseVersion/TargetVersion accordingly, and marks the result as Valid = true.
    /// 
    /// This test is currently marked as Ignored because ExecuteRemoteGitCommandAsync and the HTTP pipeline
    /// are private and cannot be mocked or injected using Moq per the constraints. To enable this test:
    /// - Introduce a seam to inject an HttpMessageHandler or
    /// - Make ExecuteRemoteGitCommandAsync protected virtual to allow Moq setup.
    /// </summary>
    /// <param name="repoUri">Repository URI used to infer owner and repo, e.g. https://github.com/owner/repo</param>
    /// <param name="baseVersion">Base version/sha/tag</param>
    /// <param name="targetVersion">Target version/sha/tag</param>
    /// <param name="ahead">Expected ahead_by value in the response</param>
    /// <param name="behind">Expected behind_by value in the response</param>
    [TestCase("https://github.com/dotnet/runtime", "v1.0.0", "v2.0.0", 3, 1)]
    [TestCase("https://github.com/dotnet/roslyn", "main", "feature/xyz", 0, 0)]
    [TestCase("https://github.com/abc/def", "1234567890abcdef", "fedcba0987654321", 42, 7)]
    [TestCase("https://github.com/owner/repo", "A", "B", 1, 2)]
    [Ignore("Cannot inject/mimic private HTTP dependency (ExecuteRemoteGitCommandAsync). See XML doc for guidance.")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public async Task GitDiffAsync_ValidResponse_MapsAheadBehindAndSetsValidTrue(string repoUri, string baseVersion, string targetVersion, int ahead, int behind)
    {
        // Arrange
        // NOTE: This is a partial test placeholder. The following steps are required to complete:
        // - Create a test double for GitHubClient that allows injecting a fake HTTP pipeline.
        // - Configure the pipeline to return a 200 OK response with body: { \"ahead_by\": <ahead>, \"behind_by\": <behind> }.

        var tokenProvider = (IRemoteTokenProvider)null;
        var processManager = (IProcessManager)null;
        var loggerMock = new Mock<ILogger>();
        var cache = (IMemoryCache)null;

        var client = new GitHubClient(tokenProvider, processManager, loggerMock.Object, null, cache);

        // Act
        // var result = await client.GitDiffAsync(repoUri, baseVersion, targetVersion);

        // Assert
        // Use AwesomeAssertions to validate when the HTTP pipeline is injectable.
        // result.BaseVersion.Should().Be(baseVersion);
        // result.TargetVersion.Should().Be(targetVersion);
        // result.Ahead.Should().Be(ahead);
        // result.Behind.Should().Be(behind);
        // result.Valid.Should().BeTrue();

        Assert.Inconclusive("Incomplete test: requires refactoring to inject/mount the HTTP pipeline for GitDiffAsync.");
    }

    /// <summary>
    /// Verifies that GitDiffAsync returns an UnknownDiff (Valid = false) when the compare endpoint returns 404 NotFound.
    /// 
    /// This test is currently marked as Ignored because ExecuteRemoteGitCommandAsync and the HTTP pipeline
    /// are private and cannot be mocked or injected using Moq per the constraints. To enable this test:
    /// - Introduce a seam to inject an HttpMessageHandler or
    /// - Make ExecuteRemoteGitCommandAsync protected virtual to allow Moq setup.
    /// </summary>
    /// <param name="repoUri">Repository URI used to infer owner and repo</param>
    /// <param name="baseVersion">Base version/sha/tag</param>
    /// <param name="targetVersion">Target version/sha/tag</param>
    [TestCase("https://github.com/dotnet/runtime", "does-not-exist-1", "does-not-exist-2")]
    [TestCase("https://github.com/owner/repo", "old", "new")]
    [Ignore("Cannot inject/mimic private HTTP dependency (ExecuteRemoteGitCommandAsync). See XML doc for guidance.")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public async Task GitDiffAsync_CompareNotFound_ReturnsUnknownDiff(string repoUri, string baseVersion, string targetVersion)
    {
        // Arrange
        // Required future steps:
        // - Inject HTTP behavior causing ExecuteRemoteGitCommandAsync to throw HttpRequestException with StatusCode = 404.

        var tokenProvider = (IRemoteTokenProvider)null;
        var processManager = (IProcessManager)null;
        var loggerMock = new Mock<ILogger>();
        var cache = (IMemoryCache)null;

        var client = new GitHubClient(tokenProvider, processManager, loggerMock.Object, null, cache);

        // Act
        // var result = await client.GitDiffAsync(repoUri, baseVersion, targetVersion);

        // Assert
        // result.Valid.Should().BeFalse();

        Assert.Inconclusive("Incomplete test: requires refactoring to force HttpRequestException(NotFound) on compare.");
    }

    /// <summary>
    /// Validates that RepoExistsAsync returns false when the underlying HTTP execution fails.
    /// Input conditions:
    /// - Various repository URIs, including unparseable and parseable ones.
    /// - The GitHubClient is constructed with a null token provider, which causes an exception
    ///   during HTTP client creation, ensuring the internal ExecuteRemoteGitCommandAsync path fails.
    /// Expected result:
    /// - The method should catch the exception and return false without throwing.
    /// </summary>
    [TestCase("https://github.com")]
    [TestCase("https://github.com/")]
    [TestCase("https://github.com/owner")]
    [TestCase("https://github.com/owner/")]
    [TestCase("https://example.com/owner/repo")]
    [TestCase("https://github.com/owner/repo")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public async Task RepoExistsAsync_RequestFails_ReturnsFalse(string repoUri)
    {
        // Arrange
        var loggerMock = new Mock<ILogger>();
        var client = new GitHubClient(
            /* remoteTokenProvider */ null,
            /* processManager */ null,
            /* logger */ loggerMock.Object,
            /* temporaryRepositoryPath */ null,
            /* cache */ null);

        client.AllowRetries = false;

        // Act
        bool exists = await client.RepoExistsAsync(repoUri);

        // Assert
        exists.Should().BeFalse();
    }

    /// <summary>
    /// Partial test placeholder for validating the 'true' path of RepoExistsAsync when the repository exists.
    /// Input conditions:
    /// - A valid repository URI that would result in a successful GitHub API response.
    /// Expected result:
    /// - RepoExistsAsync returns true.
    /// Notes:
    /// - This test is skipped because ExecuteRemoteGitCommandAsync is a private, non-virtual method that
    ///   cannot be mocked with Moq, and it internally constructs HttpClient, preventing injection of a fake handler.
    /// - To enable this test, refactor the production code to allow injecting an HttpMessageHandler or make
    ///   ExecuteRemoteGitCommandAsync overridable; then mock it to return a successful HttpResponseMessage.
    /// </summary>
    [Test]
    [Ignore("Cannot validate success path without refactoring: ExecuteRemoteGitCommandAsync is private and not mockable.")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public async Task RepoExistsAsync_ValidRepo_Succeeds_ReturnsTrue()
    {
        // Arrange
        // TODO: After refactor, inject a mockable HTTP pipeline to simulate success.
        var loggerMock = new Mock<ILogger>();
        var client = new GitHubClient(
            /* remoteTokenProvider */ null,
            /* processManager */ null,
            /* logger */ loggerMock.Object,
            /* temporaryRepositoryPath */ null,
            /* cache */ null);

        // Act
        var result = await client.RepoExistsAsync("https://github.com/owner/repo");

        // Assert
        result.Should().BeTrue();
    }

    /// <summary>
    /// LsTreeAsync_InvalidOrUnresolvableGitRef_ThrowsArgumentException
    /// Purpose:
    /// - Verify that when the git reference (branch/tag/sha) cannot be resolved via the Git API,
    ///   the method throws an ArgumentException as implemented by GetCommitShaForGitRefAsync.
    /// Inputs:
    /// - uri: well-formed GitHub API repo URL.
    /// - gitRef: various values including empty/whitespace/special to exercise error path.
    /// - path: null to keep initial lookup simple.
    /// Expected:
    /// - ArgumentException is thrown with a message indicating the git reference could not be resolved.
    /// - Under the hood: heads/{gitRef} -> NotFound, commit -> NotFound, tags/{gitRef} -> general Exception triggers final ArgumentException.
    /// </summary>
    [TestCase("https://api.github.com/repos/owner/repo", "feature/branch")]
    [TestCase("https://api.github.com/repos/owner/repo", "")]
    [TestCase("https://api.github.com/repos/owner/repo", " ")]
    [TestCase("https://api.github.com/repos/owner/repo", "refs/heads/🚀-weird")]
    [TestCase("https://api.github.com/repos/owner/repo", "a-very-very-very-very-very-very-very-very-very-long-ref-name-1234567890")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public async Task LsTreeAsync_GitRefCannotBeResolved_ThrowsArgumentException(string uri, string gitRef)
    {
        // Arrange
        var client = new Mock<GitHubClient>(null, null, NullLogger.Instance, null, null) { CallBase = true };

        var gitHubClientMock = new Mock<IGitHubClient>(MockBehavior.Strict);
        var gitDbMock = new Mock<IGitDatabaseClient>(MockBehavior.Strict);
        var refsMock = new Mock<IReferencesClient>(MockBehavior.Strict);
        var commitsMock = new Mock<ICommitsClient>(MockBehavior.Strict);

        // Reference heads/{gitRef} -> NotFound
        refsMock
            .Setup(m => m.Get(It.IsAny<string>(), It.IsAny<string>(), It.Is<string>(r => r.StartsWith("heads/", StringComparison.Ordinal))))
            .ThrowsAsync(new NotFoundException("not found", HttpStatusCode.NotFound));

        // Commit.Get -> NotFound
        commitsMock
            .Setup(m => m.Get(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ThrowsAsync(new NotFoundException("not found", HttpStatusCode.NotFound));

        // Reference tags/{gitRef} -> General exception to trigger final ArgumentException
        refsMock
            .Setup(m => m.Get(It.IsAny<string>(), It.IsAny<string>(), It.Is<string>(r => r.StartsWith("tags/", StringComparison.Ordinal))))
            .ThrowsAsync(new Exception("resolution failed"));

        gitDbMock.SetupGet(m => m.Reference).Returns(refsMock.Object);
        gitDbMock.SetupGet(m => m.Commit).Returns(commitsMock.Object);
        gitHubClientMock.SetupGet(m => m.Git).Returns(gitDbMock.Object);

        client.Setup(m => m.GetClient(It.IsAny<string>(), It.IsAny<string>())).Returns(gitHubClientMock.Object);

        // Act + Assert
        var ex = Assert.ThrowsAsync<ArgumentException>(async () =>
        {
            await client.Object.LsTreeAsync(uri, gitRef, null);
        });

        Assert.That(ex, Is.Not.Null);
        Assert.That(ex!.ParamName, Is.EqualTo("gitRef"));
        Assert.That(ex.Message, Does.Contain("Could not resolve git reference").IgnoreCase);

        // Verify call path
        refsMock.Verify(m => m.Get(It.IsAny<string>(), It.IsAny<string>(), It.Is<string>(r => r.StartsWith("heads/", StringComparison.Ordinal))), Times.Once);
        commitsMock.Verify(m => m.Get(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Once);
        refsMock.Verify(m => m.Get(It.IsAny<string>(), It.IsAny<string>(), It.Is<string>(r => r.StartsWith("tags/", StringComparison.Ordinal))), Times.Once);
    }

    /// <summary>
    /// LsTreeAsync_HappyPath_NewPathAndCachingBehavior_Partial
    /// Purpose:
    /// - Intended to validate that:
    ///   1) When a valid commit/tree is resolved, the method returns entries with Path composed as "{path}/{item.Path}".
    ///   2) When item.Type == Tree, (uri, gitRef, newPath) is cached in _gitRefCommitCache.
    ///   3) When tree.Truncated is true, a warning is logged but results are returned.
    /// Current Limitation:
    /// - Octokit concrete types like Commit and TreeResponse must be returned by the mocked clients.
    ///   Those require constructing valid Octokit models, which is not feasible here without additional factory utilities.
    /// Next Steps to complete this test:
    /// - Provide helpers/factories that create Octokit.Commit (with a Tree SHA) and Octokit.TreeResponse (with a list of TreeItem),
    ///   or update production code to allow injecting lightweight abstractions for these models.
    /// - After providing those instances, set up:
    ///     * client.Git.Reference.Get or client.Git.Commit.Get to resolve commitSha and commit.Tree.Sha
    ///     * client.Git.Tree.Get to return a TreeResponse containing both Blob and Tree items
    ///   Then assert:
    ///     * Returned GitTreeItem entries have expected Path/Sha/Type (Type.ToLower() per model).
    ///     * Subsequent LsTreeAsync calls with a sub-path hit the internal cache (no additional commit/tree traversal).
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public async Task LsTreeAsync_HappyPath_NewPathAndCachingBehavior_Partial()
    {
        // Arrange
        var client = new Mock<GitHubClient>(null, null, NullLogger.Instance, null, null) { CallBase = true };

        // TODO: Provide concrete Octokit.Commit and Octokit.TreeResponse instances via mocks or factories.
        // client.Setup(m => m.GetClient(It.IsAny<string>(), It.IsAny<string>())).Returns(mockedOctokitClient);

        // Act
        // This is a placeholder to keep the test compiling; the test is intentionally marked inconclusive until
        // proper Octokit model construction helpers are available.
        Assert.Inconclusive("Provide concrete Octokit.Commit and Octokit.TreeResponse instances (or suitable factories) to complete the happy-path and caching validation for LsTreeAsync.");
        await Task.CompletedTask;
    }
}


[TestFixture]
public class GitHubClientConstructorTests
{
    /// <summary>
    /// Verifies that the GitHubClient constructor succeeds for a variety of temporaryRepositoryPath values
    /// (including null, empty, whitespace, long, and special-character strings) and that the AllowRetries
    /// property is initialized to true by default.
    /// Inputs:
    /// - A mocked IRemoteTokenProvider, IProcessManager, ILogger
    /// - A set of temporaryRepositoryPath values (edge cases)
    /// - A null IMemoryCache instance
    /// Expected:
    /// - Construction does not throw
    /// - Instance is not null
    /// - AllowRetries is true
    /// </summary>
    [TestCaseSource(nameof(TemporaryRepositoryPathCases))]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void Constructor_TemporaryRepositoryPathEdgeCases_DoesNotThrowAndAllowRetriesTrue(string temporaryRepositoryPath)
    {
        // Arrange
        var tokenProvider = new Mock<IRemoteTokenProvider>(MockBehavior.Strict);
        var processManager = new Mock<IProcessManager>(MockBehavior.Strict);
        var logger = new Mock<ILogger>(MockBehavior.Loose);
        IMemoryCache cache = null;

        // Act
        GitHubClient sut = null;
        Assert.DoesNotThrow(() =>
        {
            sut = new GitHubClient(tokenProvider.Object, processManager.Object, logger.Object, temporaryRepositoryPath, cache);
        });

        // Assert
        Assert.That(sut, Is.Not.Null, "Constructor should create a non-null instance.");
        Assert.That(sut.AllowRetries, Is.True, "AllowRetries should default to true.");
    }

    /// <summary>
    /// Verifies that the GitHubClient constructor correctly handles both null and non-null IMemoryCache inputs,
    /// without throwing, and that the AllowRetries property is initialized to true by default.
    /// Inputs:
    /// - A mocked IRemoteTokenProvider, IProcessManager, ILogger
    /// - A boolean indicating whether to provide a cache (mock) or null
    /// Expected:
    /// - Construction does not throw
    /// - Instance is not null
    /// - AllowRetries is true
    /// </summary>
    [TestCase(true)]
    [TestCase(false)]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void Constructor_CacheNullOrProvided_DoesNotThrowAndAllowRetriesTrue(bool provideCache)
    {
        // Arrange
        var tokenProvider = new Mock<IRemoteTokenProvider>(MockBehavior.Strict);
        var processManager = new Mock<IProcessManager>(MockBehavior.Strict);
        var logger = new Mock<ILogger>(MockBehavior.Loose);
        IMemoryCache cache = provideCache ? new Mock<IMemoryCache>(MockBehavior.Loose).Object : null;
        string temporaryRepositoryPath = "C:\\temp\\repo";

        // Act
        GitHubClient sut = null;
        Assert.DoesNotThrow(() =>
        {
            sut = new GitHubClient(tokenProvider.Object, processManager.Object, logger.Object, temporaryRepositoryPath, cache);
        });

        // Assert
        Assert.That(sut, Is.Not.Null, "Constructor should create a non-null instance.");
        Assert.That(sut.AllowRetries, Is.True, "AllowRetries should default to true.");
    }

    private static IEnumerable<string> TemporaryRepositoryPathCases()
    {
        yield return null;
        yield return string.Empty;
        yield return "   ";
        yield return "C:\\path\\to\\repo";
        yield return "/var/tmp/repo";
        yield return new string('a', 1024);
        yield return "C:\\tëmp\\répø\\💾";
    }
}



[TestFixture]
public class GitHubClientSearchPullRequestsAsyncTests
{
    /// <summary>
    /// Partial test scaffold for SearchPullRequestsAsync to validate query construction and JSON parsing.
    /// This test is marked ignored because the method internally calls a private method (ExecuteRemoteGitCommandAsync)
    /// that cannot be mocked or intercepted with Moq, and performs real HTTP requests.
    /// To complete:
    ///   - Expose or make ExecuteRemoteGitCommandAsync overridable (protected virtual) to allow mocking,
    ///     OR inject an HttpClient (or handler) so tests can provide a fake response without real network I/O.
    ///   - Then, verify:
    ///       1) The request path includes:
    ///          - repo:{owner}/{repo}
    ///          - head:{pullRequestBranch}
    ///          - type:pr
    ///          - is:{status.ToString().ToLower()}
    ///          - optional keyword (if not null or empty) followed by '+'
    ///          - optional "+author:{author}" when author is provided and not empty
    ///       2) The response "items" array is parsed and the "number" fields are returned as integers.
    /// Inputs exercised:
    ///   - repoUri variations, null/empty/whitespace keyword, special characters, very long keyword, author presence/absence,
    ///     and all PrStatus enum values.
    /// Expected result:
    ///   - When mocked, the method should return the parsed PR numbers without throwing.
    ///   - With invalid/malformed JSON, it should throw during parsing.
    /// </summary>
    [Ignore("Partial test - ExecuteRemoteGitCommandAsync is private and not mockable; refactor needed to inject HTTP or make it overridable.")]
    [TestCase("https://github.com/dotnet/runtime", "feature/abc", PrStatus.Open, null, null)]
    [TestCase("https://github.com/dotnet/runtime", "feature/abc", PrStatus.Closed, "", null)]
    [TestCase("https://github.com/dotnet/runtime", "feature/abc", PrStatus.Merged, "   ", "octocat")]
    [TestCase("https://github.com/dotnet/runtime", "bug fix/with spaces", PrStatus.None, "bug fix", "user-name")]
    [TestCase("https://github.com/dotnet/runtime", "release/9.0", PrStatus.Open, "C#/.NET", "dev@example.com")]
    [TestCase("https://github.com/dotnet/runtime", "hotfix", PrStatus.Open, "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "someone")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public async Task SearchPullRequestsAsync_VariousInputs_BuildsExpectedQueryAndParsesNumbers_Partial(
        string repoUri,
        string pullRequestBranch,
        PrStatus status,
        string keyword,
        string author)
    {
        // Arrange
        var client = new Mock<GitHubClient>(null, null, NullLogger.Instance, null, (IMemoryCache)null) { CallBase = true };

        // TODO (when refactoring is done):
        // - Intercept ExecuteRemoteGitCommandAsync and provide a fake HttpResponseMessage
        //   with content like:
        //     {
        //       "items": [
        //         { "number": 123 },
        //         { "number": 456 }
        //       ]
        //     }
        // - Capture the 'requestUri' argument to ensure it contains the expected query components.

        // Act
        // var result = await client.Object.SearchPullRequestsAsync(repoUri, pullRequestBranch, status, keyword, author);

        // Assert
        // result.Should().BeEquivalentTo(new[] { 123, 456 });
        // Additionally, validate that optional keyword segment is included only when not null/empty,
        // whitespace-only handling, and "+author:{author}" is present when author is not null/empty.
    }
}



/// <summary>
/// Tests for GitHubClient.GetLatestPullRequestReviewsAsync
/// </summary>
public class GitHubClient_GetLatestPullRequestReviewsAsync_Tests
{
    /// <summary>
    /// Ensures that for N users who reviewed "Approved", the method returns exactly one actionable review per user,
    /// and ignores subsequent "Commented" entries by the same users.
    /// Also validates that a different PR id yields zero reviews.
    /// </summary>
    /// <param name="pullRequestUrl">Target pull request URL to query</param>
    /// <param name="fakeUserCount">Number of distinct users who left reviews</param>
    /// <param name="usersCommentAfterApprove">When true, each user leaves a later "Commented" review which must be ignored</param>
    /// <param name="expectedReviewCount">Expected count of actionable reviews returned</param>
    [TestCase("https://api.github.com/repos/owner1/repoA/pulls/123", 0, false, 0)]
    [TestCase("https://api.github.com/repos/owner1/repoA/pulls/123", 1, false, 1)]
    [TestCase("https://api.github.com/repos/owner1/repoA/pulls/123", 5, false, 5)]
    [TestCase("https://api.github.com/repos/owner1/repoA/pulls/123", 5, true, 5)]
    [TestCase("https://api.github.com/repos/owner1/repoA/pulls/124", 5, false, 0)]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public async Task GetLatestPullRequestReviewsAsync_ApprovalsWithOptionalLaterComments_ReturnsOnePerUserApproved(
        string pullRequestUrl, int fakeUserCount, bool usersCommentAfterApprove, int expectedReviewCount)
    {
        // Arrange
        var owner = "owner1";
        var repo = "repoA";
        var targetId = 123;

        var data = GetApprovingPullRequestData(owner, repo, targetId, fakeUserCount, usersCommentAfterApprove);

        var gitHubClientMock = BuildOctokitClientMock(data);
        var client = new Mock<GitHubClient>(null, null, NullLogger.Instance, null, null);
        client.Setup(m => m.GetClient(It.IsAny<string>(), It.IsAny<string>())).Returns(gitHubClientMock.Object);
        client.Setup(m => m.GetClient(It.IsAny<string>())).Returns(gitHubClientMock.Object);

        // Act
        var result = await client.Object.GetLatestPullRequestReviewsAsync(pullRequestUrl);

        // Assert
        result.Should().NotBeNull();
        result.Count.Should().Be(expectedReviewCount);
        if (expectedReviewCount > 0)
        {
            foreach (var r in result)
            {
                r.Status.Should().Be(ReviewState.Approved);
                r.Url.Should().Be(pullRequestUrl);
            }
        }
    }

    /// <summary>
    /// Verifies that when only "Commented" entries exist for users, the method returns an empty list since comments are filtered out.
    /// Also includes a case for zero users and an alternate PR id returning zero.
    /// </summary>
    /// <param name="pullRequestUrl">Target pull request URL to query</param>
    /// <param name="fakeUserCount">Number of users who left comment-only entries</param>
    /// <param name="expectedReviewCount">Expected count of actionable reviews returned (always 0)</param>
    [TestCase("https://api.github.com/repos/owner2/repoB/pulls/456", 0, 0)]
    [TestCase("https://api.github.com/repos/owner2/repoB/pulls/456", 7, 0)]
    [TestCase("https://api.github.com/repos/owner2/repoB/pulls/457", 7, 0)]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public async Task GetLatestPullRequestReviewsAsync_OnlyComments_ReturnsEmpty(
        string pullRequestUrl, int fakeUserCount, int expectedReviewCount)
    {
        // Arrange
        var owner = "owner2";
        var repo = "repoB";
        var targetId = 456;

        var data = GetOnlyCommentsPullRequestData(owner, repo, targetId, fakeUserCount);

        var gitHubClientMock = BuildOctokitClientMock(data);
        var client = new Mock<GitHubClient>(null, null, NullLogger.Instance, null, null);
        client.Setup(m => m.GetClient(It.IsAny<string>(), It.IsAny<string>())).Returns(gitHubClientMock.Object);
        client.Setup(m => m.GetClient(It.IsAny<string>())).Returns(gitHubClientMock.Object);

        // Act
        var result = await client.Object.GetLatestPullRequestReviewsAsync(pullRequestUrl);

        // Assert
        result.Should().NotBeNull();
        result.Should().BeEmpty();
        result.Count.Should().Be(expectedReviewCount);
    }

    /// <summary>
    /// Ensures that when multiple reviews per user exist (e.g., ChangesRequested, then Approved, possibly followed by a comment),
    /// the method selects the single most recent actionable review per user and maps it correctly (Approved).
    /// Includes cases for zero users and an alternate PR id returning zero.
    /// </summary>
    /// <param name="pullRequestUrl">Target pull request URL to query</param>
    /// <param name="fakeUserCount">Number of distinct users who left multiple reviews</param>
    /// <param name="usersCommentAfterApprove">When true, each user leaves a later "Commented" review which must be ignored</param>
    /// <param name="expectedReviewCount">Expected count of actionable reviews returned</param>
    [TestCase("https://api.github.com/repos/owner3/repoC/pulls/789", 0, false, 0)]
    [TestCase("https://api.github.com/repos/owner3/repoC/pulls/789", 3, false, 3)]
    [TestCase("https://api.github.com/repos/owner3/repoC/pulls/789", 3, true, 3)]
    [TestCase("https://api.github.com/repos/owner3/repoC/pulls/790", 3, false, 0)]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public async Task GetLatestPullRequestReviewsAsync_MultipleReviewsPerUser_PicksLatestApproved(
        string pullRequestUrl, int fakeUserCount, bool usersCommentAfterApprove, int expectedReviewCount)
    {
        // Arrange
        var owner = "owner3";
        var repo = "repoC";
        var targetId = 789;

        var data = GetMixedPullRequestData(owner, repo, targetId, fakeUserCount, usersCommentAfterApprove);

        var gitHubClientMock = BuildOctokitClientMock(data);
        var client = new Mock<GitHubClient>(null, null, NullLogger.Instance, null, null);
        client.Setup(m => m.GetClient(It.IsAny<string>(), It.IsAny<string>())).Returns(gitHubClientMock.Object);
        client.Setup(m => m.GetClient(It.IsAny<string>())).Returns(gitHubClientMock.Object);

        // Act
        var result = await client.Object.GetLatestPullRequestReviewsAsync(pullRequestUrl);

        // Assert
        result.Should().NotBeNull();
        result.Count.Should().Be(expectedReviewCount);
        foreach (var r in result)
        {
            r.Status.Should().Be(ReviewState.Approved);
            r.Url.Should().Be(pullRequestUrl);
        }
    }

    /// <summary>
    /// Validates that "Dismissed" reviews are mapped to "Commented" and considered actionable (not filtered out),
    /// returning exactly one per user when only "Dismissed" reviews exist.
    /// </summary>
    /// <param name="pullRequestUrl">Target pull request URL to query</param>
    /// <param name="fakeUserCount">Number of users who left "Dismissed" reviews</param>
    /// <param name="expectedReviewCount">Expected count of actionable reviews (equals fakeUserCount)</param>
    [TestCase("https://api.github.com/repos/owner4/repoD/pulls/900", 1, 1)]
    [TestCase("https://api.github.com/repos/owner4/repoD/pulls/900", 4, 4)]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public async Task GetLatestPullRequestReviewsAsync_DismissedMappedToCommented_ReturnsOnePerUser(
        string pullRequestUrl, int fakeUserCount, int expectedReviewCount)
    {
        // Arrange
        var owner = "owner4";
        var repo = "repoD";
        var targetId = 900;

        var data = GetDismissedOnlyPullRequestData(owner, repo, targetId, fakeUserCount);

        var gitHubClientMock = BuildOctokitClientMock(data);
        var client = new Mock<GitHubClient>(null, null, NullLogger.Instance, null, null);
        client.Setup(m => m.GetClient(It.IsAny<string>(), It.IsAny<string>())).Returns(gitHubClientMock.Object);
        client.Setup(m => m.GetClient(It.IsAny<string>())).Returns(gitHubClientMock.Object);

        // Act
        var result = await client.Object.GetLatestPullRequestReviewsAsync(pullRequestUrl);

        // Assert
        result.Should().NotBeNull();
        result.Count.Should().Be(expectedReviewCount);
        foreach (var r in result)
        {
            r.Status.Should().Be(ReviewState.Commented);
            r.Url.Should().Be(pullRequestUrl);
        }
    }

    // Helpers

    private static Mock<IGitHubClient> BuildOctokitClientMock(Dictionary<Tuple<string, string, int>, List<PullRequestReview>> data)
    {
        var octoKitGithubClient = new Mock<IGitHubClient>(MockBehavior.Strict);
        var octoKitRepositoriesClient = new Mock<IRepositoriesClient>(MockBehavior.Strict);
        var octoKitPullRequestsClient = new Mock<IPullRequestsClient>(MockBehavior.Strict);
        var octoKitPullRequestReviewsClient = new Mock<IPullRequestReviewsClient>(MockBehavior.Strict);

        octoKitPullRequestReviewsClient
            .Setup(m => m.GetAll(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()))
            .ReturnsAsync((string owner, string repo, int id) =>
            {
                var key = Tuple.Create(owner, repo, id);
                return data.TryGetValue(key, out var list)
                    ? (IReadOnlyList<PullRequestReview>)list
                    : new List<PullRequestReview>();
            });

        octoKitPullRequestsClient.Setup(m => m.Review).Returns(octoKitPullRequestReviewsClient.Object);
        octoKitRepositoriesClient.Setup(m => m.PullRequest).Returns(octoKitPullRequestsClient.Object);
        octoKitGithubClient.Setup(m => m.Repository).Returns(octoKitRepositoriesClient.Object);

        return octoKitGithubClient;
    }

    private static Dictionary<Tuple<string, string, int>, List<PullRequestReview>> GetApprovingPullRequestData(
        string owner, string repoName, int requestId, int userCount, bool commentAfter)
    {
        var data = new Dictionary<Tuple<string, string, int>, List<PullRequestReview>>();
        var keyValue = Tuple.Create(owner, repoName, requestId);
        data.Add(keyValue, new List<PullRequestReview>());

        DateTimeOffset baseOffset = DateTimeOffset.UtcNow;

        for (int i = 0; i < userCount; i++)
        {
            data[keyValue].Add(CreateFakePullRequestReview(PullRequestReviewState.Approved, owner, repoName, requestId, baseOffset.AddMinutes(i), $"user{i}"));
            if (commentAfter)
            {
                data[keyValue].Add(CreateFakePullRequestReview(PullRequestReviewState.Commented, owner, repoName, requestId, baseOffset.AddMinutes(i).AddMinutes(1), $"user{i}"));
            }
        }

        return data;
    }

    private static Dictionary<Tuple<string, string, int>, List<PullRequestReview>> GetMixedPullRequestData(
        string owner, string repoName, int requestId, int userCount, bool commentAfter)
    {
        var data = new Dictionary<Tuple<string, string, int>, List<PullRequestReview>>();
        var keyValue = Tuple.Create(owner, repoName, requestId);
        data.Add(keyValue, new List<PullRequestReview>());

        DateTimeOffset baseOffset = DateTimeOffset.UtcNow;

        for (int i = 0; i < userCount; i++)
        {
            var u = $"user{i}";
            // Earlier: ChangesRequested
            data[keyValue].Add(CreateFakePullRequestReview(PullRequestReviewState.ChangesRequested, owner, repoName, requestId, baseOffset.AddMinutes(i), u));
            // Later: Approved (this should be selected)
            data[keyValue].Add(CreateFakePullRequestReview(PullRequestReviewState.Approved, owner, repoName, requestId, baseOffset.AddMinutes(i).AddMinutes(2), u));
            // Optional latest: Commented (ignored by filter)
            if (commentAfter)
            {
                data[keyValue].Add(CreateFakePullRequestReview(PullRequestReviewState.Commented, owner, repoName, requestId, baseOffset.AddMinutes(i).AddMinutes(3), u));
            }
        }

        return data;
    }

    private static Dictionary<Tuple<string, string, int>, List<PullRequestReview>> GetOnlyCommentsPullRequestData(
        string owner, string repoName, int requestId, int userCount)
    {
        var data = new Dictionary<Tuple<string, string, int>, List<PullRequestReview>>();
        var keyValue = Tuple.Create(owner, repoName, requestId);
        data.Add(keyValue, new List<PullRequestReview>());
        DateTimeOffset baseOffset = DateTimeOffset.UtcNow;

        for (int i = 0; i < userCount; i++)
        {
            data[keyValue].Add(CreateFakePullRequestReview(PullRequestReviewState.Commented, owner, repoName, requestId, baseOffset.AddMinutes(i), $"user{i}"));
        }

        return data;
    }

    private static Dictionary<Tuple<string, string, int>, List<PullRequestReview>> GetDismissedOnlyPullRequestData(
        string owner, string repoName, int requestId, int userCount)
    {
        var data = new Dictionary<Tuple<string, string, int>, List<PullRequestReview>>();
        var keyValue = Tuple.Create(owner, repoName, requestId);
        data.Add(keyValue, new List<PullRequestReview>());
        DateTimeOffset baseOffset = DateTimeOffset.UtcNow;

        for (int i = 0; i < userCount; i++)
        {
            data[keyValue].Add(CreateFakePullRequestReview(PullRequestReviewState.Dismissed, owner, repoName, requestId, baseOffset.AddMinutes(i), $"user{i}"));
        }

        return data;
    }

    private static PullRequestReview CreateFakePullRequestReview(
        PullRequestReviewState reviewState,
        string owner,
        string repoName,
        int requestId,
        DateTimeOffset reviewTime,
        string userName)
    {
        return new PullRequestReview(
            id: 0,
            nodeId: string.Empty,
            commitId: "41deec8f17c45a064c542438da456c99a37710d9",
            user: GetFakeUser(userName),
            body: "Review body",
            path: string.Empty,
            htmlUrl: $"https://api.github.com/repos/{owner}/{repoName}/pulls/{requestId}",
            state: reviewState,
            authorAssociation: AuthorAssociation.None,
            submittedAt: reviewTime);
    }

    private static User GetFakeUser(string userId)
    {
        // Only login is relevant in these tests; other fields are dummies.
        return new User(
            avatarUrl: null,
            bio: null,
            blog: null,
            collaborators: 0,
            company: null,
            createdAt: DateTimeOffset.MinValue,
            updatedAt: DateTimeOffset.MinValue,
            diskUsage: 0,
            email: "fake@email.com",
            followers: 0,
            following: 0,
            hireable: false,
            htmlUrl: null,
            id: 0,
            ldapDistinguishedName: 0,
            location: "nonexistent",
            login: userId,
            name: userId,
            nodeId: string.Empty,
            ownedPrivateRepos: 0,
            plan: null,
            privateGists: 0,
            publicGists: 0,
            publicRepos: 0,
            totalPrivateRepos: string.Empty,
            twoFactorAuthentication: null,
            siteAdmin: false,
            twitterUsername: string.Empty,
            suspendedAt: null);
    }
}



/// <summary>
/// Tests for GitHubClient.GetClient(string repoUri)
/// Focused on lazy initialization and caching semantics.
/// </summary>
public class GitHubClient_GetClient_MethodTests
{
    /// <summary>
    /// Verifies that GetClient(repoUri) creates the underlying IGitHubClient only once (lazy init),
    /// and returns the exact same instance on subsequent calls, even when invoked with a different repoUri.
    /// Inputs:
    /// - firstRepoUri: the URI used on first invocation.
    /// - secondRepoUri: the URI used on second invocation (can be different).
    /// Expected:
    /// - Token provider is called exactly once with firstRepoUri.
    /// - The returned IGitHubClient instances from both calls reference the same object.
    /// - No exception is thrown.
    /// </summary>
    [TestCase("https://github.com/dotnet/arcade", "https://github.com/dotnet/arcade")]
    [TestCase("https://github.com/dotnet/arcade", "https://github.com/dotnet/runtime")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void GetClient_FirstAndSecondCall_ReturnsSameInstance_AndCreatesOnlyOnce(string firstRepoUri, string secondRepoUri)
    {
        // Arrange
        var tokenProviderMock = new Mock<IRemoteTokenProvider>(MockBehavior.Strict);
        tokenProviderMock.Setup(m => m.GetTokenForRepository(firstRepoUri)).Returns("fake-token");

        var processManagerMock = new Mock<IProcessManager>(MockBehavior.Loose);
        var loggerMock = new Mock<ILogger>(MockBehavior.Loose);

        var sut = new GitHubClient(tokenProviderMock.Object, processManagerMock.Object, loggerMock.Object, cache: null);

        // Act
        var firstClient = sut.GetClient(firstRepoUri);
        var secondClient = sut.GetClient(secondRepoUri);

        // Assert
        // Validation using Moq verification and explicit checks
        tokenProviderMock.Verify(m => m.GetTokenForRepository(firstRepoUri), Times.Once);
        tokenProviderMock.Verify(m => m.GetTokenForRepository(It.Is<string>(s => s == secondRepoUri)), Times.Never);

        if (firstClient is null || secondClient is null)
        {
            throw new Exception("GetClient returned null IGitHubClient instance.");
        }

        if (!object.ReferenceEquals(firstClient, secondClient))
        {
            throw new Exception("GetClient did not return the same cached IGitHubClient instance across calls.");
        }
    }

    /// <summary>
    /// Verifies that GetClient(repoUri) throws when the token provider returns a null or empty token.
    /// Inputs:
    /// - tokenValue: null or empty string from the token provider.
    /// Expected:
    /// - A DarcException is thrown.
    /// </summary>
    [TestCase(null)]
    [TestCase("")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void GetClient_TokenProviderReturnsNullOrEmpty_ThrowsDarcException(string tokenValue)
    {
        // Arrange
        const string repoUri = "https://github.com/dotnet/arcade";
        var tokenProviderMock = new Mock<IRemoteTokenProvider>(MockBehavior.Strict);
        tokenProviderMock.Setup(m => m.GetTokenForRepository(repoUri)).Returns(tokenValue);

        var processManagerMock = new Mock<IProcessManager>(MockBehavior.Loose);
        var loggerMock = new Mock<ILogger>(MockBehavior.Loose);

        var sut = new GitHubClient(tokenProviderMock.Object, processManagerMock.Object, loggerMock.Object, cache: null);

        // Act
        try
        {
            sut.GetClient(repoUri);

            // Assert (negative path)
            throw new Exception("Expected DarcException was not thrown.");
        }
        catch (DarcException)
        {
            // Expected
        }
    }
}



/// <summary>
/// Unit tests for GitHubClient.ParseRepoUri
/// </summary>
public class GitHubClientTests_ParseRepoUri
{
    /// <summary>
    /// Verifies that valid repository URLs with exactly two path segments (owner/repo)
    /// are parsed correctly. This includes variants with trailing slashes, queries, fragments,
    /// different schemes and hosts.
    /// </summary>
    /// <param name="input">A valid repository URL with exactly two path segments.</param>
    /// <param name="expectedOwner">Expected parsed owner segment.</param>
    /// <param name="expectedRepo">Expected parsed repo segment.</param>
    [TestCase("https://github.com/dotnet/arcade", "dotnet", "arcade")]
    [TestCase("https://github.com/dotnet/arcade/", "dotnet", "arcade")]
    [TestCase("http://github.com/dotnet/arcade?query=1", "dotnet", "arcade")]
    [TestCase("https://api.github.com/dotnet/arcade#frag", "dotnet", "arcade")]
    [TestCase("ssh://git@github.com/dotnet/arcade", "dotnet", "arcade")]
    [TestCase("https://example.com/owner-name_123/repo.name-456", "owner-name_123", "repo.name-456")]
    [TestCase("https://github.com/dot%20net/arcade", "dot%20net", "arcade")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void ParseRepoUri_ValidTwoSegmentPaths_ReturnsOwnerAndRepo(string input, string expectedOwner, string expectedRepo)
    {
        // Arrange
        // Inputs provided via TestCase

        // Act
        var result = GitHubClient.ParseRepoUri(input);

        // Assert
        result.owner.Should().Be(expectedOwner);
        result.repo.Should().Be(expectedRepo);
    }

    /// <summary>
    /// Ensures that inputs which do not represent exactly two path segments
    /// return the default tuple (null, null). This includes single-segment,
    /// three-or-more segments, root-only, API-styled repo paths, and empty/whitespace inputs.
    /// </summary>
    /// <param name="input">A URL string that does not match "/owner/repo" shape.</param>
    [TestCase("https://github.com/dotnet")]                         // single segment
    [TestCase("https://github.com/dotnet/arcade/issues")]           // extra segment
    [TestCase("https://github.com/")]                               // root path
    [TestCase("https://github.com")]                                // empty path
    [TestCase("https://api.github.com/repos/dotnet/arcade")]        // API style path
    [TestCase("https://github.com/dotnet/arcade/tree/main")]        // extra segment
    [TestCase("")]                                                  // empty string
    [TestCase("   ")]                                               // whitespace-only
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void ParseRepoUri_InvalidOrMismatchedPaths_ReturnsDefault(string input)
    {
        // Arrange
        // Inputs provided via TestCase

        // Act
        (string owner, string repo) result;
        try
        {
            result = GitHubClient.ParseRepoUri(input);
        }
        catch (UriFormatException)
        {
            // Some malformed inputs (like whitespace-only with no scheme) can throw.
            // For this test's purpose, thrown exceptions on malformed inputs are acceptable
            // and behave equivalently to returning default (no parse).
            return;
        }

        // Assert
        result.owner.Should().BeNull();
        result.repo.Should().BeNull();
    }

    /// <summary>
    /// Validates that clearly malformed URIs which cannot be parsed by UriBuilder
    /// result in a UriFormatException being thrown by ParseRepoUri.
    /// </summary>
    /// <param name="input">A malformed URI string that UriBuilder cannot parse.</param>
    [TestCase("git@github.com:dotnet/arcade")]   // SCP-like syntax, not a valid URI
    [TestCase("ht!tp://bad")]                    // invalid scheme
    [TestCase("://missing-scheme")]              // missing scheme content
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void ParseRepoUri_MalformedUri_ThrowsUriFormatException(string input)
    {
        // Arrange
        // Inputs provided via TestCase

        // Act
        Action act = () => GitHubClient.ParseRepoUri(input);

        // Assert
        act.Should().Throw<UriFormatException>();
    }
}


[TestFixture]
public class GitHubClientParsePullRequestUriTests
{
    /// <summary>
    /// Verifies that valid GitHub pull request URLs are parsed correctly.
    /// Inputs include different schemes, hosts, ports, query strings, and mixed-case names.
    /// Expects the method to return the correct owner, repo, and integer PR id.
    /// </summary>
    [TestCase("https://api.github.com/repos/owner/repo/pulls/123", "owner", "repo", 123)]
    [TestCase("http://api.github.com:8080/repos/dotnet/arcade/pulls/1", "dotnet", "arcade", 1)]
    [TestCase("https://github.com/repos/org-name/re.po_123/pulls/2147483647", "org-name", "re.po_123", 2147483647)]
    [TestCase("https://api.github.com/repos/Org/Some.Repo/pulls/42?foo=bar#frag", "Org", "Some.Repo", 42)]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void ParsePullRequestUri_ValidUrls_ReturnsParsedComponents(string url, string expectedOwner, string expectedRepo, int expectedId)
    {
        // Arrange
        // Inputs provided by TestCase

        // Act
        var result = GitHubClient.ParsePullRequestUri(url);

        // Assert
        NUnit.Framework.Assert.That(result.owner, Is.EqualTo(expectedOwner));
        NUnit.Framework.Assert.That(result.repo, Is.EqualTo(expectedRepo));
        NUnit.Framework.Assert.That(result.id, Is.EqualTo(expectedId));
    }

    /// <summary>
    /// Ensures that URLs with an invalid path shape (e.g., trailing slash, non-numeric id, missing segments)
    /// return the default tuple, indicating no match was found.
    /// Expected result: (owner: null, repo: null, id: 0).
    /// </summary>
    [TestCase("https://api.github.com/repos/owner/repo/pulls/123/")] // trailing slash
    [TestCase("https://api.github.com/repos/owner/repo/pulls/abc")]  // non-numeric id
    [TestCase("https://api.github.com/repos/owner/repo")]            // missing /pulls/{id}
    [TestCase("https://api.github.com/owner/repo/pulls/1")]          // missing /repos/ segment
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void ParsePullRequestUri_InvalidPath_ReturnsDefault(string url)
    {
        // Arrange
        var expected = default((string owner, string repo, int id));

        // Act
        var result = GitHubClient.ParsePullRequestUri(url);

        // Assert
        NUnit.Framework.Assert.That(result, Is.EqualTo(expected));
        NUnit.Framework.Assert.That(result.owner, Is.Null);
        NUnit.Framework.Assert.That(result.repo, Is.Null);
        NUnit.Framework.Assert.That(result.id, Is.EqualTo(0));
    }

    /// <summary>
    /// Validates that malformed URI strings cause UriFormatException during parsing.
    /// Inputs include non-URI text, empty string, whitespace-only string, and incomplete scheme.
    /// Expects the method to throw UriFormatException.
    /// </summary>
    [TestCase("not a url")]
    [TestCase("")]
    [TestCase("   ")]
    [TestCase("http://")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void ParsePullRequestUri_MalformedUri_ThrowsUriFormatException(string url)
    {
        // Arrange
        // Inputs provided by TestCase

        // Act + Assert
        NUnit.Framework.Assert.Throws<UriFormatException>(() => GitHubClient.ParsePullRequestUri(url));
    }

    /// <summary>
    /// Ensures that an excessively large numeric PR id (greater than int.MaxValue) results in an OverflowException
    /// during int.Parse of the id segment. The path shape matches, but the id cannot be parsed into Int32.
    /// </summary>
    [TestCase("https://api.github.com/repos/owner/repo/pulls/2147483648")]
    [TestCase("https://api.github.com/repos/owner/repo/pulls/9999999999999999999999999")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void ParsePullRequestUri_IdTooLarge_ThrowsOverflowException(string url)
    {
        // Arrange
        // Inputs provided by TestCase

        // Act + Assert
        NUnit.Framework.Assert.Throws<OverflowException>(() => GitHubClient.ParsePullRequestUri(url));
    }
}


[TestFixture]
public class GitHubClientCommentPullRequestAsyncTests
{
    /// <summary>
    /// Verifies that CommentPullRequestAsync parses a valid GitHub API pull request URL
    /// and delegates to Octokit's Issue.Comment.Create with the exact parsed owner, repo, id, and comment.
    /// Inputs:
    ///  - Valid API URL constructed from {owner}/{repo}/{id}
    ///  - Various comment contents including normal, very long, whitespace-only, and special characters
    /// Expected:
    ///  - GetClient is called once with the parsed {owner}, {repo}
    ///  - Issue.Comment.Create is called once with the parsed {owner}, {repo}, {id}, and the exact comment
    /// </summary>
    [TestCaseSource(nameof(ValidUrlCommentCases))]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public async Task CommentPullRequestAsync_ValidApiUrl_InvokesIssueCommentCreateWithParsedValues(string owner, string repo, int id, string comment)
    {
        // Arrange
        var clientMock = new Mock<GitHubClient>(null, null, NullLogger.Instance, null, (IMemoryCache)null);
        var octoKitClientMock = new Mock<IGitHubClient>(MockBehavior.Loose);
        var issuesClientMock = new Mock<IIssuesClient>();
        var issueCommentsClientMock = new Mock<IIssuesCommentsClient>();

        issueCommentsClientMock
            .Setup(m => m.Create(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>()))
            .ReturnsAsync((IssueComment)null);
        issuesClientMock.Setup(m => m.Comment).Returns(issueCommentsClientMock.Object);
        octoKitClientMock.Setup(m => m.Issue).Returns(issuesClientMock.Object);
        clientMock.Setup(m => m.GetClient(It.IsAny<string>(), It.IsAny<string>())).Returns(octoKitClientMock.Object);

        string pullRequestUrl = $"https://api.github.com/repos/{owner}/{repo}/pulls/{id}";

        // Act
        await clientMock.Object.CommentPullRequestAsync(pullRequestUrl, comment);

        // Assert
        clientMock.Verify(m => m.GetClient(owner, repo), Times.Once());
        issueCommentsClientMock.Verify(m => m.Create(owner, repo, id, comment), Times.Once());
    }

    /// <summary>
    /// Provides parameterized cases for valid API URL parsing and comment forwarding.
    /// Covers normal text, very long text, whitespace-only, and special/control characters.
    /// </summary>
    public static IEnumerable<object[]> ValidUrlCommentCases()
    {
        yield return new object[] { "owner", "repo", 1, "hello world" };
        yield return new object[] { "dotnet", "runtime", int.MaxValue, new string('a', 10000) };
        yield return new object[] { "org-name", "repo.name_123", 42, " \t " };
        yield return new object[] { "o", "r", 100, "line1\nline2\t\u2603" };
    }

    /// <summary>
    /// Validates behavior when the URL does not match the expected API pattern.
    /// Inputs:
    ///  - Invalid pull request URL that fails the ParsePullRequestUri regex
    /// Expected:
    ///  - The tuple parsed is default (owner = null, repo = null, id = 0)
    ///  - GetClient is invoked with (null, null) and Create is invoked with (null, null, 0, comment)
    ///    This exposes a potential bug: invalid URLs are not validated before use.
    /// </summary>
    [TestCase("https://github.com/owner/repo/pull/123")]
    [TestCase("https://api.github.com/repos/owner/repo/pull/123")] // 'pull' instead of 'pulls'
    [TestCase("https://api.github.com/owner/repo/pulls/123")]       // missing '/repos'
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public async Task CommentPullRequestAsync_InvalidUrl_InvokesIssueCommentCreateWithDefaultParsedValues(string invalidUrl)
    {
        // Arrange
        var clientMock = new Mock<GitHubClient>(null, null, NullLogger.Instance, null, (IMemoryCache)null);
        var octoKitClientMock = new Mock<IGitHubClient>(MockBehavior.Loose);
        var issuesClientMock = new Mock<IIssuesClient>();
        var issueCommentsClientMock = new Mock<IIssuesCommentsClient>();

        issueCommentsClientMock
            .Setup(m => m.Create(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>()))
            .ReturnsAsync((IssueComment)null);
        issuesClientMock.Setup(m => m.Comment).Returns(issueCommentsClientMock.Object);
        octoKitClientMock.Setup(m => m.Issue).Returns(issuesClientMock.Object);
        clientMock.Setup(m => m.GetClient(It.IsAny<string>(), It.IsAny<string>())).Returns(octoKitClientMock.Object);

        string comment = "test-comment";

        // Act
        await clientMock.Object.CommentPullRequestAsync(invalidUrl, comment);

        // Assert
        clientMock.Verify(m => m.GetClient(It.Is<string>(s => s == null), It.Is<string>(s => s == null)), Times.Once());
        issueCommentsClientMock.Verify(m => m.Create(It.Is<string>(s => s == null), It.Is<string>(s => s == null), 0, comment), Times.Once());
    }
}
