using System;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;
using Moq;
using Xunit;

namespace Microsoft.DotNet.Web.Authentication.Tests
{
    public class SimpleSignOnTests
    {
        private const string LoginPath = "/cookie/login";
        private const string LogoutPath = "/cookie/logout";
        private const string AuthCallbackUrl = "/test/callback";
        private const string ChallengeScheme = "TestScheme";

        [Fact]
        public async Task OtherUrlIsIgnored()
        {
            Mock<IAuthenticationService> auth = new Mock<IAuthenticationService>();

            await RunMockAuthContext(auth, "https://example.text/some/url");

            auth.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task BasicSignOnIssuesChallenge()
        {
            Mock<IAuthenticationService> auth = new Mock<IAuthenticationService>();
            auth.Setup(a => a.ChallengeAsync(It.IsAny<HttpContext>(), It.IsAny<string>(), It.IsAny<AuthenticationProperties>()))
                .Returns(Task.CompletedTask)
                .Verifiable();

            await RunMockAuthContext(auth, "https://example.text/cookie/login");

            auth.Verify(a => a.ChallengeAsync(
                It.IsAny<HttpContext>(),
                ChallengeScheme,
                It.Is<AuthenticationProperties>(p => p.RedirectUri == "https://example.text/test/callback"))
            );
            auth.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task BasicSignInWithTargetIssuesChallengeWithTarget()
        {
            Mock<IAuthenticationService> auth = new Mock<IAuthenticationService>();
            auth.Setup(a => a.ChallengeAsync(It.IsAny<HttpContext>(), It.IsAny<string>(), It.IsAny<AuthenticationProperties>()))
                .Returns(Task.CompletedTask)
                .Verifiable();

            await RunMockAuthContext(auth, "https://example.text/cookie/login?r=" + Uri.EscapeDataString("https://example.text/target"));

            auth.Verify(a => a.ChallengeAsync(
                It.IsAny<HttpContext>(),
                ChallengeScheme,
                It.Is<AuthenticationProperties>(p => p.RedirectUri == "https://example.text/test/callback?r=" + Uri.EscapeDataString("https://example.text/target")))
            );
            auth.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task BasicSignOutSignsOutAndRedirectsToRoot()
        {
            Mock<IAuthenticationService> auth = new Mock<IAuthenticationService>();
            auth.Setup(a => a.SignOutAsync(It.IsAny<HttpContext>(), It.IsAny<string>(), It.IsAny<AuthenticationProperties>()))
                .Returns(Task.CompletedTask)
                .Verifiable();

            var ctx = await RunMockAuthContext(auth, "https://example.text/cookie/logout");

            Assert.Equal(302, ctx.Response.StatusCode);
            Assert.Equal("https://example.text/", ctx.Response.Headers.TryGetValue(HeaderNames.Location, out var locationHeader) ? locationHeader.ToString() : "<NONE>");

            auth.Verify(a => a.SignOutAsync(
                It.IsAny<HttpContext>(),
                ChallengeScheme,
                It.IsAny<AuthenticationProperties>()
            ));
            auth.VerifyNoOtherCalls();
        }

        private static async Task<DefaultHttpContext> RunMockAuthContext(Mock<IAuthenticationService> auth, string url)
        {
            var collection = new ServiceCollection();
            collection.AddScoped<SimpleSigninMiddleware>();
            collection.AddSingleton<IMiddlewareFactory, MiddlewareFactory>();
            collection.AddSingleton(auth.Object);
            collection.Configure<SimpleSigninOptions>(o =>
                {
                    o.AuthCallbackUrl = AuthCallbackUrl;
                    o.ChallengeScheme = ChallengeScheme;
                }
            );
            collection.Configure<CookieAuthenticationOptions>(
                IdentityConstants.ApplicationScheme,
                o =>
                {
                    o.LoginPath = LoginPath;
                    o.LogoutPath = LogoutPath;
                }
            );
            ServiceProvider provider = collection.BuildServiceProvider();
            var appBuilder = new ApplicationBuilder(provider);
            appBuilder.UseMiddleware<SimpleSigninMiddleware>();
            RequestDelegate requestDelegate = appBuilder.Build();
            using IServiceScope scope = provider.CreateScope();
            var context = new DefaultHttpContext
            {
                RequestServices = scope.ServiceProvider,
            };
            context.Request.SetUrl(url);
            await requestDelegate(context);
            return context;
        }
    }

    internal static class DefaultHttpContextExtensions
    {
        internal static void SetUrl(this HttpRequest req, string url)
        {
            UriHelper.FromAbsolute(url,
                out string scheme,
                out HostString host,
                out PathString path,
                out QueryString queryString,
                out _
            );
            req.Scheme = scheme;
            req.Host = host;
            req.Path = path;
            req.QueryString = queryString;
        }
    }
}
