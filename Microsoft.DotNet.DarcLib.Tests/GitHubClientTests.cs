using FluentAssertions;
using Microsoft.DotNet.Internal.Testing.Utility;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using Octokit;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Primitives;

namespace Microsoft.DotNet.DarcLib.Tests
{
    public class SimpleCacheEntry : ICacheEntry
    {
        private object _key;
        private object _value;
        private long? _size;

        public SimpleCacheEntry(object key)
        {
            _key = key;
        }

        public object Key => _key;

        public object Value { get => _value; set => _value = value; }
        public DateTimeOffset? AbsoluteExpiration { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
        public TimeSpan? AbsoluteExpirationRelativeToNow { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
        public TimeSpan? SlidingExpiration { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

        public IList<IChangeToken> ExpirationTokens => throw new NotImplementedException();

        public IList<PostEvictionCallbackRegistration> PostEvictionCallbacks => throw new NotImplementedException();

        public CacheItemPriority Priority { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
        public long? Size { get => _size; set => _size = value; }

        public void Dispose() { }
    }

    public class SimpleCache : IMemoryCache
    {
        public int CacheHits { get; set; }
        public int CacheMisses { get; set; }
        public ConcurrentDictionary<object, ICacheEntry> cache = new ConcurrentDictionary<object, ICacheEntry>();

        public ICacheEntry CreateEntry(object key)
        {
            var newEntry = new SimpleCacheEntry(key);
            return cache.AddOrUpdate(key, new SimpleCacheEntry(key), (existingKey, existingValue) => newEntry);
        }

        [TearDown]
        public void Dispose()
        {

        }

        public void Remove(object key)
        {
            cache.Remove(key, out ICacheEntry unused);
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

    [TestFixture]
    public class GitHubClientTests
    {
        [TestCase(true)]
        [TestCase(false)]
        public async Task TreeItemCacheTest(bool enableCache)
        {
            SimpleCache cache = enableCache ? new SimpleCache() : null;
            Mock<GitHubClient> client = new Mock<GitHubClient>(null, null, NullLogger.Instance, null, cache);

            List<(string, string, Octokit.TreeItem)> treeItemsToGet = new List<(string, string, Octokit.TreeItem)>
            {
                ("a", "b", new Octokit.TreeItem("path", "mode", Octokit.TreeType.Blob, 10, "1", "https://url")),
                ("a", "b", new Octokit.TreeItem("path", "mode", Octokit.TreeType.Blob, 10, "2", "https://url")),
                ("a", "b", new Octokit.TreeItem("path", "mode", Octokit.TreeType.Blob, 10, "3", "https://url")),
                ("a", "b", new Octokit.TreeItem("path", "mode", Octokit.TreeType.Blob, 10, "4", "https://url")),
                ("dotnet", "corefx", new Octokit.TreeItem("path", "mode", Octokit.TreeType.Blob, 10, "11", "https://url")),
                ("dotnet", "corefx", new Octokit.TreeItem("path", "mode", Octokit.TreeType.Blob, 10, "12", "https://url")),
            };

            // Mock up the github client
            var octoKitClientMock = new Mock<Octokit.IGitHubClient>();
            var octoKitGitMock = new Mock<Octokit.IGitDatabaseClient>();
            var octoKitBlobClientMock = new Mock<Octokit.IBlobsClient>();
            Octokit.Blob blob = new Octokit.Blob("foo", "content", Octokit.EncodingType.Utf8, "somesha", 10);

            foreach (var treeItem in treeItemsToGet)
            {
                octoKitBlobClientMock.Setup(m => m.Get(treeItem.Item1, treeItem.Item2, treeItem.Item3.Sha)).ReturnsAsync(blob);
            }

            octoKitGitMock.Setup(m => m.Blob).Returns(octoKitBlobClientMock.Object);
            octoKitClientMock.Setup(m => m.Git).Returns(octoKitGitMock.Object);
            client.Setup(m => m.Client).Returns(octoKitClientMock.Object);

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
            renamedTreeItem.Item3 = new Octokit.TreeItem("anotherPath",
                renamedTreeItemBlob.Mode,
                Octokit.TreeType.Blob,
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
                await client.Object.GetGitTreeItem("anotherPath", treeItemsToGet[0].Item3, treeItemsToGet[0].Item1, treeItemsToGet[0].Item2);
                cache.CacheHits.Should().Be(expectedCacheHits);
            }
        }

        [TestCase(false)]
        public async Task TreeItemCacheAbuseExceptionRetryTest(bool enableCache)
        {
            SimpleCache cache = enableCache ? new SimpleCache() : null;
            Mock<GitHubClient> client = new Mock<GitHubClient>(null, null, NullLogger.Instance, null, cache);

            List<(string, string, TreeItem)> treeItemsToGet = new List<(string, string, TreeItem)>
            {
                ("a", "b", new TreeItem("path", "mode", TreeType.Blob, 10, "1", "https://url")),
                ("a", "b", new TreeItem("path", "mode", TreeType.Blob, 10, "2", "https://url")),
                ("a", "b", new TreeItem("path", "mode", TreeType.Blob, 10, "3", "https://url")),
                ("a", "b", new TreeItem("path", "mode", TreeType.Blob, 10, "4", "https://url")),
                ("dotnet", "corefx", new TreeItem("path", "mode", TreeType.Blob, 10, "11", "https://url")),
                ("dotnet", "corefx", new TreeItem("path", "mode", TreeType.Blob, 10, "12", "https://url")),
            };

            // Mock up the github client
            var octoKitClientMock = new Mock<IGitHubClient>();
            var octoKitGitMock = new Mock<IGitDatabaseClient>();
            var octoKitBlobClientMock = new Mock<IBlobsClient>();
            Blob blob = new Blob("foo", "content", EncodingType.Utf8, "somesha", 10);

            foreach (var treeItem in treeItemsToGet)
            {
                octoKitBlobClientMock.Setup(m => m.Get(treeItem.Item1, treeItem.Item2, treeItem.Item3.Sha)).ReturnsAsync(blob);
            }

            octoKitGitMock.Setup(m => m.Blob).Returns(octoKitBlobClientMock.Object);
            octoKitClientMock.Setup(m => m.Git).Returns(octoKitGitMock.Object);
            client.Setup(m => m.Client).Returns(octoKitClientMock.Object);

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
            renamedTreeItem.Item3 = new Octokit.TreeItem("anotherPath",
                renamedTreeItemBlob.Mode,
                Octokit.TreeType.Blob,
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
                await client.Object.GetGitTreeItem("anotherPath", treeItemsToGet[0].Item3, treeItemsToGet[0].Item1, treeItemsToGet[0].Item2);
                cache.CacheHits.Should().Be(expectedCacheHits);
            }
        }

        #region GitHubClient with set-able IGitHubClient 
        /// <summary>
        /// Lacking any DI, this class lets us put a Mock IGitHubClient into something that is effectively the same,
        /// other than providing the ability to stick any IGitHubClient in as desired.
        /// </summary>
        class TestGitHubClient : GitHubClient
        {
            private IGitHubClient _client;
            public void SetGitHubClientObject(IGitHubClient value)
            {
                _client = value;
            }

            public override IGitHubClient Client
            {
                get
                {
                    return _client;
                }
            }
            public TestGitHubClient(string gitExecutable, string accessToken, ILogger logger, string temporaryRepositoryPath, IMemoryCache cache)
                : base(gitExecutable, accessToken, logger, temporaryRepositoryPath, cache)
            {
            }
        }
        #endregion

        #region Setup / Teardown

        // When adding more tests, new OctoKit mocks go here and get set up either per-fact or in 
        // GitHubClientTests_SetUp(), depending on whether they are used in multiple places.
        protected Mock<IGitHubClient> OctoKitGithubClient;
        protected Mock<IRepositoriesClient> OctoKitRepositoriesClient;
        protected Mock<IPullRequestsClient> OctoKitPullRequestsClient;
        protected Mock<IPullRequestReviewsClient> OctoKitPullRequestReviewsClient;

        TestGitHubClient GitHubClientForTest;

        [SetUp]
        public void GitHubClientTests_SetUp()
        {
            //var services = new ServiceCollection();
            OctoKitPullRequestReviewsClient = new Mock<IPullRequestReviewsClient>();

            OctoKitPullRequestsClient = new Mock<IPullRequestsClient>();
            OctoKitPullRequestsClient.SetupGet(x => x.Review).Returns(OctoKitPullRequestReviewsClient.Object);

            OctoKitRepositoriesClient = new Mock<IRepositoriesClient>();
            OctoKitRepositoriesClient.SetupGet(x => x.PullRequest).Returns(OctoKitPullRequestsClient.Object);

            OctoKitGithubClient = new Mock<IGitHubClient>();
            OctoKitGithubClient.SetupGet(x => x.Repository).Returns(OctoKitRepositoriesClient.Object);

            NUnitLogger nUnitLogger = new NUnitLogger();
            GitHubClientForTest = new TestGitHubClient("git", "fake-token", nUnitLogger, "fake-path", null);
            GitHubClientForTest.SetGitHubClientObject(OctoKitGithubClient.Object);
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
            Assert.AreEqual(reviews.Count, expectedReviewCount);
            Assert.False(reviews.Any(r => r.Status == ReviewState.ChangesRequested || r.Status == ReviewState.Rejected));
        }

        [TestCase("https://api.github.com/repos/githubclienttests/getlatestreviews/pulls/123", 0, 10)]
        [TestCase("https://api.github.com/repos/githubclienttests/getlatestreviews/pulls/124", 0, 0)]
        public async Task GetReviewsForPullRequestCommentsOnly(string pullRequestUrl, int expectedReviewCount, int fakeUserCount)
        {
            var pullRequestReviewData = GetOnlyCommentsPullRequestData("githubclienttests", "getlatestreviews", 123, fakeUserCount);
            var reviews = await GetLatestReviewsForPullRequestWrapperAsync(pullRequestReviewData, pullRequestUrl);
            Assert.AreEqual(reviews.Count, expectedReviewCount);
        }

        [TestCase("https://api.github.com/repos/githubclienttests/getmixedreviews/pulls/456", 10, 10, false)] // Happy path: 10 approvals
        [TestCase("https://api.github.com/repos/githubclienttests/getmixedreviews/pulls/456", 10, 10, true)]  // Same as above but user comments 1 minute later
        [TestCase("https://api.github.com/repos/githubclienttests/getmixedreviews/pulls/456", 0, 0, false, false)]   // No reviews yet
        [TestCase("https://api.github.com/repos/githubclienttests/getmixedreviews/pulls/457", 0, 5, false, false)]   // Reviews exist, not for this one.
        public async Task GetReviewsForPullRequestMultiPerUser(string pullRequestUrl, int expectedReviewCount, int fakeUserCount, bool usersCommentAfterApprove, bool successExpected = true)
        {
            var pullRequestReviewData = GetMixedPullRequestData("githubclienttests", "getmixedreviews", 456, fakeUserCount, usersCommentAfterApprove);
            var reviews = await GetLatestReviewsForPullRequestWrapperAsync(pullRequestReviewData, pullRequestUrl);
            Assert.AreEqual(reviews.Count, expectedReviewCount);

            if (successExpected)
            {
                Assert.False(reviews.Any(r => r.Status == ReviewState.ChangesRequested || r.Status == ReviewState.Rejected));
                Assert.GreaterOrEqual(reviews.Count, 1);
            }
            else if (reviews.Count > 0)
            {
                Assert.True(reviews.Any(r => r.Status == ReviewState.ChangesRequested || r.Status == ReviewState.Rejected));
            }
        }

        private async Task<IList<Review>> GetLatestReviewsForPullRequestWrapperAsync(Dictionary<Tuple<string, string, int>, List<PullRequestReview>> data, string pullRequestUrl)
        {
            List<PullRequestReview> fakeReviews = new List<PullRequestReview>();

            // Use Moq to put the return value 
            OctoKitPullRequestReviewsClient.Setup(x => x.GetAll(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()))
                .Callback<string, string, int>((string x, string y, int z) =>
                {
                    Tuple<string, string, int> theKey = new Tuple<string, string, int>(x, y, z);
                    if (data.ContainsKey(theKey))
                    {
                        fakeReviews.AddRange(data[theKey]);
                    }

                })
                .ReturnsAsync(fakeReviews);

            return await GitHubClientForTest.GetLatestPullRequestReviewsAsync(pullRequestUrl);
        }

        #region Functions for creating fake review data

        private Dictionary<Tuple<string, string, int>, List<PullRequestReview>> GetApprovingPullRequestData(string owner, string repoName, int requestId, int userCount, bool commentAfter)
        {
            var data = new Dictionary<Tuple<string, string, int>, List<PullRequestReview>>();
            var keyValue = new Tuple<string, string, int>(owner, repoName, requestId);
            data.Add(keyValue, new List<PullRequestReview>());
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

        private Dictionary<Tuple<string, string, int>, List<PullRequestReview>> GetMixedPullRequestData(string owner, string repoName, int requestId, int userCount, bool commentAfter)
        {
            var data = new Dictionary<Tuple<string, string, int>, List<PullRequestReview>>();
            var keyValue = new Tuple<string, string, int>(owner, repoName, requestId);
            data.Add(keyValue, new List<PullRequestReview>());
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

        private Dictionary<Tuple<string, string, int>, List<PullRequestReview>> GetOnlyCommentsPullRequestData(string owner, string repoName, int requestId, int userCount)
        {
            var data = new Dictionary<Tuple<string, string, int>, List<PullRequestReview>>();
            var keyValue = new Tuple<string, string, int>(owner, repoName, requestId);
            data.Add(keyValue, new List<PullRequestReview>());
            DateTimeOffset baseOffset = DateTimeOffset.UtcNow;

            for (int i = 0; i < userCount; i++)
            {
                data[keyValue].Add(CreateFakePullRequestReview(PullRequestReviewState.Commented, owner, repoName, requestId, baseOffset, $"username{i}"));
            }
            return data;
        }

        private PullRequestReview CreateFakePullRequestReview(PullRequestReviewState reviewState, string owner, string repoName, int requestId, DateTimeOffset reviewTime, string userName)
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

        private User GetFakeUser(string userId)
        {
            // We mostly only care about the user's login id (userId)", this ctor is huge, sorry about that.
            return new User(null, null, null, 0, null, DateTimeOffset.MinValue, DateTimeOffset.MinValue,
                0, "fake@email.com", 0, 0, false, null, 0, 0, "nonexistent", userId, userId,
                string.Empty, 0, null, 0, 0, 0, string.Empty, null, false, string.Empty, null);
        }

        #endregion
    }
}
