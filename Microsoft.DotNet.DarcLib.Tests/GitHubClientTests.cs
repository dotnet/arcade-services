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

namespace Microsoft.DotNet.DarcLib.Tests
{
    class GitHubClientTests
    {
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

        public async Task<IList<Review>> GetLatestReviewsForPullRequestWrapperAsync(Dictionary<Tuple<string, string, int>, List<PullRequestReview>> data, string pullRequestUrl)
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
