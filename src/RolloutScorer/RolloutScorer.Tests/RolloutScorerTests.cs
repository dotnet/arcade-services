using Microsoft.Azure.KeyVault;
using Microsoft.Azure.Services.AppAuthentication;
using Octokit;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Xunit;

namespace RolloutScorer.Tests
{
    public class RolloutScorerTests
    {
        private const string URI_SUBDOMAIN = "dev.";
        private const string URI_DOMAIN = "azure.com";

        private const string EXPECTED_GOOD_REDIRECT_MESSAGE = "Response status code does not indicate success: 401";
        private const string EXPECTED_BAD_HOST_REDIRECT_MESSAGE = "Bad redirect host";
        private const string EXPECTED_BAD_SCHEME_REDIRECT_MESSAGE = "Bad redirect scheme";

        private RolloutScorer _rolloutScorer;

        public static IEnumerable<object[]> GetUriTestCases()
        {
            return new List<object[]>
            {
                new object[] { "same-host-redirect", $"https://{URI_SUBDOMAIN}{URI_DOMAIN}/dnceng/redirected", EXPECTED_GOOD_REDIRECT_MESSAGE },
                new object[] { "different-subdomain-redirect", $"https://different.{URI_DOMAIN}/redirect", EXPECTED_BAD_HOST_REDIRECT_MESSAGE },
                new object[] { "no-subdomain-redirect", $"https://{URI_DOMAIN}/redirect", EXPECTED_BAD_HOST_REDIRECT_MESSAGE },
                new object[] { "different-host-redirect", $"https://evil.com/redirect", EXPECTED_BAD_HOST_REDIRECT_MESSAGE },
                new object[] { "no-https-redirect", $"http://{URI_SUBDOMAIN}{URI_DOMAIN}/dnceng/redirected", EXPECTED_BAD_SCHEME_REDIRECT_MESSAGE },
            };
        }

        public RolloutScorerTests()
        {
            _rolloutScorer = new RolloutScorer();
            Config config = Utilities.ParseConfig();
            _rolloutScorer.RolloutWeightConfig = config.RolloutWeightConfig;
            _rolloutScorer.RepoConfig = config.RepoConfigs.First();
            _rolloutScorer.AzdoConfig = config.AzdoInstanceConfigs.Find(a => a.Name == _rolloutScorer.RepoConfig.AzdoInstance);
            _rolloutScorer.Repo = _rolloutScorer.RepoConfig.Repo;
            _rolloutScorer.RolloutStartDate = DateTimeOffset.Now.AddDays(-1);

            _rolloutScorer.SetupHttpClient(new Microsoft.Azure.KeyVault.Models.SecretBundle(value: "fakePat"));
        }

        [Theory]
        [MemberData(nameof(GetUriTestCases))]
        public async Task RedirectApiTest(string requestPath, string responseUri, string expectedMessage)
        {
            Uri sameHostRedirectUri = new Uri($"https://{URI_SUBDOMAIN}{URI_DOMAIN}/dnceng/{requestPath}");
            HttpResponseMessage sameHostRedirectResponse = new HttpResponseMessage();
            sameHostRedirectResponse.Headers.Location = new Uri(responseUri);

            try
            {
                await _rolloutScorer.HandleApiRedirect(sameHostRedirectResponse, sameHostRedirectUri);
            }
            catch (HttpRequestException e)
            {
                Assert.Contains(expectedMessage, e.Message);
            }
        }

        // We're only testing Octokit because if this behavior changes, we won't know in our code and our scorecard results will be silently incorrect
        [Fact]
        public async Task OctokitReturnsOpenAndClosedIssuesTest()
        {
            SearchIssuesRequest searchIssuesRequest = new SearchIssuesRequest
            {
                Created = new DateRange(new DateTimeOffset(DateTime.Now - TimeSpan.FromDays(14)), SearchQualifierOperator.GreaterThan),
            };
            searchIssuesRequest.Repos.Add(_rolloutScorer.GithubConfig.ScorecardsGithubOrg, _rolloutScorer.GithubConfig.ScorecardsGithubRepo);

            GitHubClient githubClient = new GitHubClient(new ProductHeaderValue("rollout-scorer-tests"));

            AzureServiceTokenProvider tokenProvider = new AzureServiceTokenProvider();
            using (KeyVaultClient kv = new KeyVaultClient(new KeyVaultClient.AuthenticationCallback(tokenProvider.KeyVaultTokenCallback)))
            {
                githubClient.Credentials = new Credentials("fake", (await kv.GetSecretAsync(Utilities.KeyVaultUri, Utilities.GitHubPatSecretName)).Value);
            }

            SearchIssuesResult allIssues = await githubClient.Search.SearchIssues(searchIssuesRequest);
            searchIssuesRequest.State = ItemState.Open;
            SearchIssuesResult openIssues = await githubClient.Search.SearchIssues(searchIssuesRequest);
            searchIssuesRequest.State = ItemState.Closed;
            SearchIssuesResult closedIssues = await githubClient.Search.SearchIssues(searchIssuesRequest);

            Assert.True(openIssues.TotalCount > 0, $"Expected at least one open issue in the last two weeks; actual count was {openIssues.TotalCount}");
            Assert.True(closedIssues.TotalCount > 0, $"Expected at least one closed issue in the last two weeks; actual count was {closedIssues.TotalCount}");
            Assert.Equal(allIssues.TotalCount, openIssues.TotalCount + closedIssues.TotalCount);
        }
    }
}
