using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.DotNet.Web.Authentication.GitHub;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Internal;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.DotNet.Web.Authentication.Tests
{
    public class GitHubClaimsResolverTests
    {
        private readonly ITestOutputHelper _output;

        public GitHubClaimsResolverTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public async Task GitHubErrorFailedAuth()
        {
            using IDisposable scope = ConfigureResolver(out FakeHandler handler, out _, out GitHubClaimResolver resolver);

            handler.AddCannedResponse(HttpStatusCode.TooManyRequests,
                "https://api.github.test/user",
                new JObject
                {
                    {"error", "Rate limit exceeded"},
                    {"really-long-details", new string('*', 10000)},
                });

            await Assert.ThrowsAsync<HttpRequestException>(() => resolver.GetUserInformationClaims("FAKE-TOKEN"));

            handler.AssertCompleted();
        }

        [Fact]
        public async Task UserInformationIsPopulated()
        {
            using IDisposable scope =
                ConfigureResolver(out FakeHandler handler, out _, out GitHubClaimResolver resolver);

            handler.AddCannedResponse("https://api.github.test/user",
                new JObject
                {
                    {"id", 146},
                    {"login", "TestUser"},
                    {"email", "TestEmail@microsoft.test"},
                    {"name", "A Real Fake Name"},
                    {"url", "https://github.test/TestUser"},
                });

            (IEnumerable<Claim> claims, JObject job) = await resolver.GetUserInformation("FAKE-TOKEN");
            Assert.NotNull(job);
            var id = new ClaimsIdentity(claims, "TEST");
            var principal = new ClaimsPrincipal(id);
            string accessToken = resolver.GetAccessToken(principal);

            Assert.Equal("FAKE-TOKEN", accessToken);
            Assert.Equal("TestUser", id.Name);
            Assert.Equal("TestEmail@microsoft.test", id.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Email)?.Value);

            handler.AssertCompleted();
        }

        [Fact]
        public async Task UserInformationIsCached()
        {
            using IDisposable scope =
                ConfigureResolver(out FakeHandler handler, out _, out GitHubClaimResolver resolver);

            handler.AddCannedResponse("https://api.github.test/user",
                new JObject
                {
                    {"id", 146},
                    {"login", "TestUser"},
                    {"email", "TestEmail@microsoft.test"},
                    {"name", "A Real Fake Name"},
                    {"url", "https://github.test/TestUser"},
                });

            IEnumerable<Claim> userInformation = await resolver.GetUserInformationClaims("FAKE-TOKEN");
            var id = new ClaimsIdentity(userInformation, "TEST");
            Assert.Equal("TestUser", id.Name);

            userInformation = await resolver.GetUserInformationClaims("FAKE-TOKEN");
            id = new ClaimsIdentity(userInformation, "TEST");
            Assert.Equal("TestUser", id.Name);

            handler.AssertCompleted();
        }

        [Fact]
        public async Task ExpiredUserInformationIsFetchedAgain()
        {
            using IDisposable scope = ConfigureResolver(out FakeHandler handler,
                out TestClock clock,
                out GitHubClaimResolver resolver);

            handler.AddCannedResponse("https://api.github.test/user",
                new JObject
                {
                    {"id", 146},
                    {"login", "TestUser"},
                    {"email", "TestEmail@microsoft.test"},
                    {"name", "A Real Fake Name"},
                    {"url", "https://github.test/TestUser"},
                });
            handler.AddCannedResponse("https://api.github.test/user",
                new JObject
                {
                    {"id", 146},
                    {"login", "TestUser"},
                    {"email", "OtherEmail@microsoft.test"},
                    {"name", "A Real Fake Name"},
                    {"url", "https://github.test/TestUser"},
                });

            IEnumerable<Claim> userInformation = await resolver.GetUserInformationClaims("FAKE-TOKEN");
            var id = new ClaimsIdentity(userInformation, "TEST");
            Assert.Equal("TestUser", id.Name);
            Assert.Equal("TestEmail@microsoft.test", id.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Email)?.Value);

            clock.UtcNow = TestClock.BaseTime.AddDays(1);

            userInformation = await resolver.GetUserInformationClaims("FAKE-TOKEN");
            id = new ClaimsIdentity(userInformation, "TEST");
            Assert.Equal("TestUser", id.Name);
            Assert.Equal("OtherEmail@microsoft.test", id.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Email)?.Value);

            handler.AssertCompleted();
        }
        
        [Fact]
        public async Task MembershipClaimsArePopulated()
        {
            using IDisposable scope =
                ConfigureResolver(out FakeHandler handler, out _, out GitHubClaimResolver resolver);

            handler.AddCannedResponse("https://api.github.test/user",
                new JObject
                {
                    {"id", 146},
                    {"login", "TestUser"},
                    {"email", "TestEmail@microsoft.test"},
                    {"name", "A Real Fake Name"},
                    {"url", "https://github.test/TestUser"},
                });
            handler.AddCannedResponse("https://api.github.test/user/orgs",
                new JArray
                {
                    new JObject
                    {
                        {"id", 978},
                        {"login", "TestOrg"},
                    },
                });
            handler.AddCannedResponse("https://api.github.test/user/teams",
                new JArray
                {
                    new JObject
                    {
                        {"id", 1235},
                        {"name", "TestTeam"},
                        {
                            "organization",
                            new JObject
                            {
                                {"id", 85241},
                                {"login", "OtherOrg"},
                            }
                        },
                    },
                });

            IEnumerable<Claim> userInformation = await resolver.GetUserInformationClaims("FAKE-TOKEN");
            var id = new ClaimsIdentity(userInformation, "TEST");
            var principal = new ClaimsPrincipal(id);
            string accessToken = resolver.GetAccessToken(principal);
            IEnumerable<Claim> membershipClaims = await resolver.GetMembershipClaims(accessToken);

            var withMembershipId = new ClaimsIdentity(userInformation.Concat(membershipClaims), "TEST");
            var withMembershipPrincipal = new ClaimsPrincipal(withMembershipId);
            
            string orgRole = GitHubClaimResolver.GetOrganizationRole("TestOrg");
            string teamRole = GitHubClaimResolver.GetTeamRole("OtherOrg", "TestTeam");
            Assert.Equal("TestUser", id.Name);
            Assert.True(
                withMembershipPrincipal.IsInRole(orgRole),
                $"Test: IsInRole({orgRole})\nRoles: {string.Join(", ", withMembershipId.Claims.Where(c => c.Type == withMembershipId.RoleClaimType).Select(c => c.Value))}"
            );
            Assert.True(
                withMembershipPrincipal.IsInRole(teamRole),
                $"Test: IsInRole({teamRole})\nRoles: {string.Join(", ", withMembershipId.Claims.Where(c => c.Type == withMembershipId.RoleClaimType).Select(c => c.Value))}"
            );

            handler.AssertCompleted();
        }

        [Fact]
        public async Task MembershipClaimsAreCached()
        {
            using IDisposable scope =
                ConfigureResolver(out FakeHandler handler, out _, out GitHubClaimResolver resolver);

            handler.AddCannedResponse("https://api.github.test/user",
                new JObject
                {
                    {"id", 146},
                    {"login", "TestUser"},
                    {"email", "TestEmail@microsoft.test"},
                    {"name", "A Real Fake Name"},
                    {"url", "https://github.test/TestUser"},
                });
            handler.AddCannedResponse("https://api.github.test/user/orgs",
                new JArray
                {
                    new JObject
                    {
                        {"id", 978},
                        {"login", "TestOrg"},
                    },
                });
            handler.AddCannedResponse("https://api.github.test/user/teams",
                new JArray
                {
                    new JObject
                    {
                        {"id", 1235},
                        {"name", "TestTeam"},
                        {
                            "organization",
                            new JObject
                            {
                                {"id", 85241},
                                {"login", "OtherOrg"},
                            }
                        },
                    },
                });

            IEnumerable<Claim> userInformation = await resolver.GetUserInformationClaims("FAKE-TOKEN");
            var id = new ClaimsIdentity(userInformation, "TEST");
            var principal = new ClaimsPrincipal(id);
            string accessToken = resolver.GetAccessToken(principal);

            async Task AssertMembership()
            {
                IEnumerable<Claim> membershipClaims = await resolver.GetMembershipClaims(accessToken);

                var withMembershipId = new ClaimsIdentity(userInformation.Concat(membershipClaims), "TEST");
                var withMembershipPrincipal = new ClaimsPrincipal(withMembershipId);

                string orgRole = GitHubClaimResolver.GetOrganizationRole("TestOrg");
                string teamRole = GitHubClaimResolver.GetTeamRole("OtherOrg", "TestTeam");
                Assert.Equal("TestUser", id.Name);
                Assert.True(
                    withMembershipPrincipal.IsInRole(orgRole),
                    $"Test: IsInRole({orgRole})\nRoles: {string.Join(", ", withMembershipId.Claims.Where(c => c.Type == withMembershipId.RoleClaimType).Select(c => c.Value))}"
                );
                Assert.True(
                    withMembershipPrincipal.IsInRole(teamRole),
                    $"Test: IsInRole({teamRole})\nRoles: {string.Join(", ", withMembershipId.Claims.Where(c => c.Type == withMembershipId.RoleClaimType).Select(c => c.Value))}"
                );
            }

            await AssertMembership();
            // This will fail uncached, since there is not another copy of the canned responses
            await AssertMembership();

            handler.AssertCompleted();
        }

        [Fact]
        public async Task ExpiredMembershipClaimsAreRefreshed()
        {
            using IDisposable scope =
                ConfigureResolver(out FakeHandler handler, out TestClock clock, out GitHubClaimResolver resolver);

            handler.AddCannedResponse("https://api.github.test/user",
                new JObject
                {
                    {"id", 146},
                    {"login", "TestUser"},
                    {"email", "TestEmail@microsoft.test"},
                    {"name", "A Real Fake Name"},
                    {"url", "https://github.test/TestUser"},
                });
            handler.AddCannedResponse("https://api.github.test/user/orgs",
                new JArray
                {
                    new JObject
                    {
                        {"id", 978},
                        {"login", "OldTestOrg"},
                    },
                });
            handler.AddCannedResponse("https://api.github.test/user/teams",
                new JArray
                {
                    new JObject
                    {
                        {"id", 1235},
                        {"name", "OldTestTeam"},
                        {
                            "organization",
                            new JObject
                            {
                                {"id", 85241},
                                {"login", "OldOtherOrg"},
                            }
                        },
                    },
                });
            handler.AddCannedResponse("https://api.github.test/user/orgs",
                new JArray
                {
                    new JObject
                    {
                        {"id", 978},
                        {"login", "NewTestOrg"},
                    },
                });
            handler.AddCannedResponse("https://api.github.test/user/teams",
                new JArray
                {
                    new JObject
                    {
                        {"id", 1235},
                        {"name", "NewTestTeam"},
                        {
                            "organization",
                            new JObject
                            {
                                {"id", 85241},
                                {"login", "NewOtherOrg"},
                            }
                        },
                    },
                });

            IEnumerable<Claim> userInformation = await resolver.GetUserInformationClaims("FAKE-TOKEN");
            var id = new ClaimsIdentity(userInformation, "TEST");
            var principal = new ClaimsPrincipal(id);
            string accessToken = resolver.GetAccessToken(principal);

            async Task AssertMembership(string expectedOrg, string expectedTeamOrg, string expectedTeam)
            {
                IEnumerable<Claim> membershipClaims = await resolver.GetMembershipClaims(accessToken);

                var withMembershipId = new ClaimsIdentity(userInformation.Concat(membershipClaims), "TEST");
                var withMembershipPrincipal = new ClaimsPrincipal(withMembershipId);

                string orgRole = GitHubClaimResolver.GetOrganizationRole(expectedOrg);
                string teamRole = GitHubClaimResolver.GetTeamRole(expectedTeamOrg, expectedTeam);
                Assert.Equal("TestUser", id.Name);
                Assert.True(
                    withMembershipPrincipal.IsInRole(orgRole),
                    $"Test: IsInRole({orgRole})\nRoles: {string.Join(", ", withMembershipId.Claims.Where(c => c.Type == withMembershipId.RoleClaimType).Select(c => c.Value))}"
                );
                Assert.True(
                    withMembershipPrincipal.IsInRole(teamRole),
                    $"Test: IsInRole({teamRole})\nRoles: {string.Join(", ", withMembershipId.Claims.Where(c => c.Type == withMembershipId.RoleClaimType).Select(c => c.Value))}"
                );
            }

            await AssertMembership("OldTestOrg", "OldOtherOrg", "OldTestTeam");
            clock.UtcNow = TestClock.BaseTime.AddDays(1);
            await AssertMembership("NewTestOrg", "NewOtherOrg", "NewTestTeam");

            handler.AssertCompleted();
        }

        private IDisposable ConfigureResolver(
            out FakeHandler handler,
            out TestClock clock,
            out GitHubClaimResolver resolver)
        {
            FakeHandler localHandler = handler = new FakeHandler();
            var client = new HttpClient(handler);

            TestClock localClock = clock = new TestClock();
            var collection = new ServiceCollection();
            collection.AddSingleton<ISystemClock>(clock);
            collection.AddSingleton<AspNetCore.Authentication.ISystemClock>(clock);
            collection.AddMemoryCache(o => o.Clock = localClock);
            collection.AddLogging(l =>
            {
                l.SetMinimumLevel(LogLevel.Trace);
                l.AddProvider(new XUnitLogger(_output));
            });
            collection.AddSingleton<GitHubClaimResolver>();
            collection.Configure<GitHubAuthenticationOptions>(o =>
            {
                o.Backchannel = new HttpClient(localHandler);
                o.BackchannelHttpHandler = localHandler;
                o.AuthorizationEndpoint = "https://github.test/login/oauth/authorize";
                o.TokenEndpoint = "https://github.test/login/oauth/access_token";
                o.UserInformationEndpoint = "https://api.github.test/user";
                o.TeamsEndpoint = "https://api.github.test/user/teams";
                o.OrganizationEndpoint = "https://api.github.test/user/orgs";
            });

            ServiceProvider provider = collection.BuildServiceProvider();
            resolver = provider.GetRequiredService<GitHubClaimResolver>();

            return new Disposables(localHandler, client, provider);
        }
    }

    public sealed class Disposables : IDisposable
    {
        private List<IDisposable> _tracked = new List<IDisposable>();

        public Disposables(params IDisposable[] obj)
        {
            Track(obj);
        }

        public void Track(params IDisposable[] obj)
        {
            _tracked.AddRange(obj);
        }

        public void Dispose()
        {
            List<IDisposable> toDispose = Interlocked.Exchange(ref _tracked, null);
            if (toDispose != null)
            {
                foreach (IDisposable obj in toDispose)
                {
                    obj.Dispose();
                }
            }
        }
    }

    public class FakeHandler : HttpMessageHandler
    {
        private readonly List<(HttpStatusCode statusCode, string uri, JToken body)> _cannedResponses = new List<(HttpStatusCode statusCode, string uri, JToken body)>();

        private readonly List<(string uri, Dictionary<string, string> headers, JToken body, bool accepted)> _requests =
            new List<(string uri, Dictionary<string, string> headers, JToken body, bool accepted)>();

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Dictionary<string, string> requestHeaders =
                request.Headers.ToDictionary(h => h.Key, h => string.Join("\n", h.Value));
            JToken requestBody = null;
            if (request.Content != null)
            {
                string bodyText = await request.Content.ReadAsStringAsync();
                if (bodyText != null)
                {
                    requestBody = JToken.Parse(bodyText);
                }
            }


            int index = _cannedResponses.FindIndex(r => r.uri == request.RequestUri.AbsoluteUri);
            if (index == -1)
            {
                _requests.Add((request.RequestUri.AbsoluteUri, requestHeaders, requestBody, false));
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }

            (HttpStatusCode statusCode, _, JToken body) = _cannedResponses[index];
            _cannedResponses.RemoveAt(index);
            _requests.Add((request.RequestUri.AbsoluteUri, requestHeaders, requestBody, true));
            return new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(body.ToString(Formatting.Indented))
            };
        }
        
        public void AddCannedResponse(string uri, JToken result)
        {
            _cannedResponses.Add((HttpStatusCode.OK, uri, result));
        }

        public void AddCannedResponse(HttpStatusCode code, string uri, JToken result)
        {
            _cannedResponses.Add((code, uri, result));
        }

        public IEnumerable<string> UnexpectedRequests => _requests.Where(r => !r.accepted).Select(r => r.uri);

        public IEnumerable<string> UnusedResponses => _cannedResponses.Select(r => r.uri);

        public void AssertCompleted()
        {
            Assert.Empty(UnusedResponses);
            Assert.Empty(UnexpectedRequests);
        }
    }
}
