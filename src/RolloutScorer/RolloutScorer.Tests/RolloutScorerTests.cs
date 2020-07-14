using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Azure.KeyVault;
using Microsoft.Azure.Services.AppAuthentication;
using NUnit.Framework;
using Octokit;

namespace RolloutScorer.Tests
{
    [TestFixture]
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

        [SetUp]
        public void RolloutScorerTests_SetUp()
        {
            _rolloutScorer = new RolloutScorer();
            Config config = Utilities.ParseConfig();
            _rolloutScorer.RolloutWeightConfig = config.RolloutWeightConfig;
            _rolloutScorer.RepoConfig = config.RepoConfigs.First();
            _rolloutScorer.AzdoConfig = config.AzdoInstanceConfigs.Find(a => a.Name == _rolloutScorer.RepoConfig.AzdoInstance);
            _rolloutScorer.Repo = _rolloutScorer.RepoConfig.Repo;
            _rolloutScorer.RolloutStartDate = DateTimeOffset.Now.AddDays(-1);

            _rolloutScorer.SetupHttpClient("fakePat");
        }

        [TestCaseSource(nameof(GetUriTestCases))]
        public async Task RedirectApiTest(string requestPath, string responseUri, string expectedMessage)
        {
            Uri sameHostRedirectUri = new Uri($"https://{URI_SUBDOMAIN}{URI_DOMAIN}/dnceng/{requestPath}");
            HttpResponseMessage sameHostRedirectResponse = new HttpResponseMessage();
            sameHostRedirectResponse.Headers.Location = new Uri(responseUri);

            HttpRequestException exception = (await (((Func<Task>)(                async () => await _rolloutScorer.GetAzdoApiResponseAsync(
                    Utilities.HandleApiRedirect(sameHostRedirectResponse, sameHostRedirectUri))))).Should().ThrowAsync<HttpRequestException>()).Which;
            exception.Message.Should().Contain(expectedMessage);
        }
    }
}
