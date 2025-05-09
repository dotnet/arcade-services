// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using FluentAssertions;
using Maestro.Common;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.Models;
using Microsoft.DotNet.Internal.Testing.Utility;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using Moq;
using NUnit.Framework;
using Octokit;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

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
            if(_retryAfter.HasValue)
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
                if (data.TryGetValue(theKey, out List<PullRequestReview> value))
                {
                    fakeReviews.AddRange(value);
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
}
