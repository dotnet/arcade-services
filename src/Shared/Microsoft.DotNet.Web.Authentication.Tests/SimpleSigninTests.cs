using System;
using System.IO;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;
using Moq;
using Xunit;

namespace Microsoft.DotNet.Web.Authentication.Tests
{
    public class SimpleSigninTests
    {
        private const string LoginPath = "/cookie/login";
        private const string LogoutPath = "/cookie/logout";
        private const string AuthCallbackUrl = "/test/callback";
        private const string ChallengeScheme = "TestScheme";
        private const string Target = "https://example.test/target";
        private static readonly string s_escapedTarget = Uri.EscapeDataString(Target);

        [Fact]
        public async Task OtherUrlIsIgnored()
        {
            Mock<IAuthenticationService> auth = new Mock<IAuthenticationService>();

            await RunMockAuthContext(auth, "https://example.test/some/url");

            auth.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task BasicSignOnIssuesChallenge()
        {
            Mock<IAuthenticationService> auth = new Mock<IAuthenticationService>();
            auth.Setup(a =>
                    a.ChallengeAsync(It.IsAny<HttpContext>(), It.IsAny<string>(), It.IsAny<AuthenticationProperties>()))
                .Returns(Task.CompletedTask)
                .Verifiable();

            await RunMockAuthContext(auth, $"https://example.test{LoginPath}");

            auth.Verify(a => a.ChallengeAsync(
                It.IsAny<HttpContext>(),
                ChallengeScheme,
                It.Is<AuthenticationProperties>(p => p.RedirectUri == $"https://example.test{AuthCallbackUrl}"))
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

            await RunMockAuthContext(auth, $"https://example.test{LoginPath}?r={s_escapedTarget}");

            auth.Verify(a => a.ChallengeAsync(
                It.IsAny<HttpContext>(),
                ChallengeScheme,
                It.Is<AuthenticationProperties>(p => p.RedirectUri ==
                    $"https://example.test{AuthCallbackUrl}?r={s_escapedTarget}"))
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

            var ctx = await RunMockAuthContext(auth, $"https://example.test{LogoutPath}");

            Assert.Equal(302, ctx.Response.StatusCode);
            Assert.Equal("/", ctx.Response.Headers.TryGetValue(HeaderNames.Location, out var locationHeader) ? locationHeader.ToString() : "<NONE>");

            auth.Verify(a => a.SignOutAsync(
                It.IsAny<HttpContext>(),
                ChallengeScheme,
                It.IsAny<AuthenticationProperties>()
            ));
            auth.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task CallbackTriggersCallbackToRoot()
        {
            Mock<IAuthenticationService> auth = new Mock<IAuthenticationService>();
            auth.Setup(a => a.SignInAsync(
                    It.IsAny<HttpContext>(),
                    It.IsAny<string>(),
                    It.IsAny<ClaimsPrincipal>(),
                    It.IsAny<AuthenticationProperties>()
                ))
                .Returns(Task.CompletedTask)
                .Verifiable();

            var user = new ClaimsPrincipal(
                new ClaimsIdentity(
                    new[]
                    {
                        new Claim(ClaimTypes.Name, "TestName")
                    }
                )
            );

            var ctx = await RunMockAuthContext(auth, $"https://example.test{AuthCallbackUrl}", c => c.User = user);

            Assert.Equal(302, ctx.Response.StatusCode);
            Assert.Equal("/", ctx.Response.Headers.TryGetValue(HeaderNames.Location, out var locationHeader) ? locationHeader.ToString() : "<NONE>");

            auth.Verify(a => a.SignInAsync(
                It.IsAny<HttpContext>(),
                It.IsAny<string>(),
                It.Is<ClaimsPrincipal>(p => p == user),
                It.IsAny<AuthenticationProperties>()
            ));

            auth.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task CallbackWithTargetTriggersCallbackToTarget()
        {
            Mock<IAuthenticationService> auth = new Mock<IAuthenticationService>();
            auth.Setup(a => a.SignInAsync(
                    It.IsAny<HttpContext>(),
                    It.IsAny<string>(),
                    It.IsAny<ClaimsPrincipal>(),
                    It.IsAny<AuthenticationProperties>()
                ))
                .Returns(Task.CompletedTask)
                .Verifiable();

            var user = new ClaimsPrincipal(
                new ClaimsIdentity(
                    new[]
                    {
                        new Claim(ClaimTypes.Name, "TestName")
                    }
                )
            );

            var ctx = await RunMockAuthContext(auth, $"https://example.test{AuthCallbackUrl}?r={s_escapedTarget}", c => c.User = user);

            Assert.Equal(302, ctx.Response.StatusCode);
            Assert.Equal(Target, ctx.Response.Headers.TryGetValue(HeaderNames.Location, out var locationHeader) ? locationHeader.ToString() : "<NONE>");

            auth.Verify(a => a.SignInAsync(
                It.IsAny<HttpContext>(),
                It.IsAny<string>(),
                It.Is<ClaimsPrincipal>(p => p == user),
                It.IsAny<AuthenticationProperties>()
            ));

            auth.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task FailedCallbackReturnsError()
        {
            Mock<IAuthenticationService> auth = new Mock<IAuthenticationService>();

            var user = new ClaimsPrincipal(
                new ClaimsIdentity(
                    new[]
                    {
                        new Claim(ClaimTypes.Name, "TestName")
                    }
                )
            );

            using var stream = new MemoryStream();

            DefaultHttpContext ctx = await RunMockAuthContext(
                auth,
                $"https://example.test{AuthCallbackUrl}?remoteError=ExampleErrorMessage",
                c =>
                {
                    c.User = user;
                    c.Response.Body = stream;
                }
            );

            Assert.Equal(400, ctx.Response.StatusCode);
            Assert.Equal("ExampleErrorMessage", Encoding.UTF8.GetString(stream.ToArray()));

            auth.VerifyNoOtherCalls();
        }

        private static async Task<DefaultHttpContext> RunMockAuthContext(Mock<IAuthenticationService> auth, string url, Action<HttpContext> configureContext = null)
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

            configureContext?.Invoke(context);

            await requestDelegate(context);
            return context;
        }
    }
}
