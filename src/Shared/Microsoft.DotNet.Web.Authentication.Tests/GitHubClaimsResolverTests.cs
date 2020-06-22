using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.DotNet.GitHub.Authentication;
using Microsoft.DotNet.Web.Authentication.GitHub;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Internal;
using Microsoft.Extensions.Logging;
using Moq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Octokit;
using Octokit.Internal;
using Xunit;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace Microsoft.DotNet.Web.Authentication.Tests
{
    public class GitHubClaimsResolverTests
    {
        private readonly ITestOutputHelper _output;

        public GitHubClaimsResolverTests(ITestOutputHelper output)
        {
            _output = output;
        }

        private IResponse MockResponse()
        {
            var m = new Mock<IResponse>();
            m.Setup(r => r.ApiInfo)
                .Returns(new ApiInfo(new Dictionary<string, Uri>(), Array.Empty<string>(), Array.Empty<string>(), null, new RateLimit(100, 0, DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds())));
            return m.Object;
        }

        private User MockUser(int id, string login, string email, string name, string url)
        {
            return new User(
                default,
                default,
                default,
                default,
                default,
                default,
                default,
                default,
                email,
                default,
                default,
                default,
                default,
                default,
                id,
                default,
                login,
                name,
                default,
                default,
                default,
                default,
                default,
                default,
                url,
                default,
                default,
                default,
                default);
        }

        private Organization MockOrganization(int id, string login)
        {
            return new Organization(
                default,
                default,
                default,
                default,
                default,
                default,
                default,
                default,
                default,
                default,
                default,
                default,
                default,
                id,
                default,
                default,
                login,
                default,
                default,
                default,
                default,
                default,
                default,
                default,
                default);
        }

        private Team MockTeam(int id, string name, Organization org)
        {
            return new Team(
                default,
                id,
                default,
                default,
                name,
                default,
                default,
                default,
                default,
                default,
                org,
                default,
                default);
        }

        [Fact]
        public async Task GitHubErrorFailedAuth()
        {
            using IDisposable scope = ConfigureResolver(out Mock<IGitHubClient> mock, out _, out GitHubClaimResolver resolver);

            mock.Setup(c => c.User.Current())
                .ThrowsAsync(new RateLimitExceededException(MockResponse()));

            await Assert.ThrowsAsync<RateLimitExceededException>(() => resolver.GetUserInformationClaims("FAKE-TOKEN"));

            mock.Verify(c => c.User.Current(), Times.Once);
            mock.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task UserInformationIsPopulated()
        {
            using IDisposable scope =
                ConfigureResolver(out Mock<IGitHubClient> mock, out _, out GitHubClaimResolver resolver);

            mock.Setup(c => c.User.Current())
                .ReturnsAsync(MockUser(
                    146,
                    "TestUser",
                    "TestEmail@microsoft.test",
                    "A Real Fake Name",
                    "https://github.com/TestUser"));

            (IEnumerable<Claim> claims, User user) = await resolver.GetUserInformation("FAKE-TOKEN");
            Assert.NotNull(user);
            var id = new ClaimsIdentity(claims, "TEST");
            var principal = new ClaimsPrincipal(id);
            string accessToken = resolver.GetAccessToken(principal);

            Assert.Equal("FAKE-TOKEN", accessToken);
            Assert.Equal("TestUser", id.Name);
            Assert.Equal("TestEmail@microsoft.test", id.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Email)?.Value);

            mock.Verify(c => c.User.Current(), Times.Once);
            mock.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task ExpiredUserInformationIsFetchedAgain()
        {
            using IDisposable scope = ConfigureResolver(
                out Mock<IGitHubClient> mock,
                out TestClock clock,
                out GitHubClaimResolver resolver);

            int userTimes = 0;
            mock.Setup(c => c.User.Current())
                .ReturnsAsync(() =>
                {
                    userTimes++;
                    return userTimes switch
                    {
                        1 => MockUser(146, "TestUser", "TestEmail@microsoft.test", "A Real Fake Name", "https://github.com/TestUser"),
                        2 => MockUser(146, "TestUser", "OtherEmail@microsoft.test", "A Real Fake Name", "https://github.com/TestUser"),
                        _ => throw new XunitException("user fetched too many times"),
                    };
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

            mock.Verify(c => c.User.Current(), Times.Exactly(2));
            mock.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task UserInformationIsCached()
        {
            using IDisposable scope =
                ConfigureResolver(out Mock<IGitHubClient> mock, out _, out GitHubClaimResolver resolver);

            var called = false;
            mock.Setup(c => c.User.Current())
                .ReturnsAsync(() =>
                {
                    Assert.False(called);
                    called = true;
                    return MockUser(
                        146,
                        "TestUser",
                        "TestEmail@microsoft.test",
                        "A Real Fake Name",
                        "https://github.com/TestUser");
                });

            IEnumerable<Claim> userInformation = await resolver.GetUserInformationClaims("FAKE-TOKEN");
            var id = new ClaimsIdentity(userInformation, "TEST");
            Assert.Equal("TestUser", id.Name);

            // Combining the proven behaviors of GitHubErrorFailedAuth and ExpiredUserInformationIsFetchedAgain
            // we know this would crash if it attempted another lookup
            // * ExpiredUserInformationIsFetchedAgain proves each response is only used once
            // * GitHubErrorFailedAuth proves that the 404 we'll get (because we used all the responses now) will throw
            // So this not throwing and returning a valid answer proves it must have pulled from a cache
            userInformation = await resolver.GetUserInformationClaims("FAKE-TOKEN");
            id = new ClaimsIdentity(userInformation, "TEST");
            Assert.Equal("TestUser", id.Name);

            mock.Verify(c =>c.User.Current(), Times.Once);
            mock.VerifyNoOtherCalls();
        }
        
        [Fact]
        public async Task MembershipClaimsArePopulated()
        {
            using IDisposable scope =
                ConfigureResolver(out Mock<IGitHubClient> mock, out _, out GitHubClaimResolver resolver);

            mock.Setup(c => c.User.Current())
                .ReturnsAsync(MockUser(146, "TestUser", "TeamEmail@microsoft.test", "A Real Fake Name", "https://github.com/TestUser"));
            mock.Setup(c => c.Organization.GetAllForCurrent())
                .ReturnsAsync(new[]
                {
                    MockOrganization(978, "TestOrg"),
                });
            mock.Setup(c => c.Organization.Team.GetAllForCurrent())
                .ReturnsAsync(new[]
                {
                    MockTeam(1235, "TestTeam", MockOrganization(85241, "OtherOrg"))
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

            mock.Verify(c => c.User.Current(), Times.Once);
            mock.Verify(c => c.Organization.GetAllForCurrent(), Times.Once);
            mock.Verify(c => c.Organization.Team.GetAllForCurrent(), Times.Once);
            mock.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task MembershipClaimsAreCached()
        {
            using IDisposable scope =
                ConfigureResolver(out Mock<IGitHubClient> mock, out _, out GitHubClaimResolver resolver);

            mock.Setup(c => c.User.Current())
                .ReturnsAsync(MockUser(146, "TestUser", "TeamEmail@microsoft.test", "A Real Fake Name", "https://github.com/TestUser"));
            mock.Setup(c => c.Organization.GetAllForCurrent())
                .ReturnsAsync(new[]
                {
                    MockOrganization(978, "TestOrg"),
                });
            mock.Setup(c => c.Organization.Team.GetAllForCurrent())
                .ReturnsAsync(new[]
                {
                    MockTeam(1235, "TestTeam", MockOrganization(85241, "OtherOrg"))
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
            await AssertMembership();

            // These will fail if the response wasn't cached, because the methods will be called more than once.
            mock.Verify(c => c.User.Current(), Times.Once);
            mock.Verify(c => c.Organization.GetAllForCurrent(), Times.Once);
            mock.Verify(c => c.Organization.Team.GetAllForCurrent(), Times.Once);
            mock.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task ExpiredMembershipClaimsAreRefreshed()
        {
            using IDisposable scope =
                ConfigureResolver(out Mock<IGitHubClient> mock, out TestClock clock, out GitHubClaimResolver resolver);

            mock.Setup(c => c.User.Current())
                .ReturnsAsync(MockUser(146, "TestUser", "TeamEmail@microsoft.test", "A Real Fake Name", "https://github.com/TestUser"));
            int orgTimes = 0;
            mock.Setup(c => c.Organization.GetAllForCurrent())
                .ReturnsAsync(() =>
                {
                    orgTimes++;
                    return orgTimes switch
                    {
                        1 => new[] {MockOrganization(978, "OldTestOrg")},
                        2 => new[] {MockOrganization(978, "NewTestOrg")},
                        _ => throw new XunitException("orgs fetched too many times"),
                    };
                });
            int teamTimes = 0;
            mock.Setup(c => c.Organization.Team.GetAllForCurrent())
                .ReturnsAsync(() =>
                {
                    teamTimes++;
                    return teamTimes switch
                    {
                        1 => new[] {MockTeam(1235, "OldTestTeam", MockOrganization(85241, "OldOtherOrg"))},
                        2 => new[] {MockTeam(1235, "NewTestTeam", MockOrganization(85241, "NewOtherOrg"))},
                        _ => throw new XunitException("teams fetched too many times"),
                    };
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

            mock.Verify(c => c.User.Current(), Times.Once);
            mock.Verify(c => c.Organization.GetAllForCurrent(), Times.Exactly(2));
            mock.Verify(c => c.Organization.Team.GetAllForCurrent(), Times.Exactly(2));
            mock.VerifyNoOtherCalls();
        }

        private IDisposable ConfigureResolver(
            out Mock<IGitHubClient> clientMock,
            out TestClock clock,
            out GitHubClaimResolver resolver)
        {
            Mock<IGitHubClient> gitHubClient = clientMock = new Mock<IGitHubClient>(MockBehavior.Strict);
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
            var clientFactoryMock = new Mock<IGitHubClientFactory>();
            clientFactoryMock.Setup(f => f.CreateGitHubClient(It.IsAny<string>()))
                .Returns((string token) => gitHubClient.Object);
            collection.AddSingleton(clientFactoryMock.Object);
            collection.Configure<GitHubClientOptions>(o =>
            {
                o.ProductHeader = new ProductHeaderValue("TEST", "1.0");
            });

            ServiceProvider provider = collection.BuildServiceProvider();
            resolver = provider.GetRequiredService<GitHubClaimResolver>();

            return new Disposables(provider);
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
}
