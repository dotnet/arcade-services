using System;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.WebEncoders.Testing;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.DotNet.Web.Authentication.Tests
{
    public class DefaultAuthorizeActionModelConventionTests
    {
        private readonly ITestOutputHelper _output;

        public DefaultAuthorizeActionModelConventionTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [InlineData("no/no", HttpStatusCode.Unauthorized)]
        [InlineData("no/anonymous", HttpStatusCode.OK, "No:Anonymous:Value")]
        [InlineData("no/any", HttpStatusCode.Unauthorized)]
        [InlineData("no/role", HttpStatusCode.Unauthorized)]
        [InlineData("anonymous/no", HttpStatusCode.OK, "Anonymous:No:Value")]
        [InlineData("anonymous/anonymous", HttpStatusCode.OK, "Anonymous:Anonymous:Value")]
        [InlineData("anonymous/any", HttpStatusCode.OK, "Anonymous:Any:Value")]
        [InlineData("anonymous/role", HttpStatusCode.OK, "Anonymous:Role:Value")]
        [InlineData("any/no", HttpStatusCode.Unauthorized)]
        [InlineData("any/anonymous", HttpStatusCode.OK, "Any:Anonymous:Value")]
        [InlineData("any/any", HttpStatusCode.Unauthorized)]
        [InlineData("any/role", HttpStatusCode.Unauthorized)]
        [InlineData("role/no", HttpStatusCode.Unauthorized)]
        [InlineData("role/anonymous", HttpStatusCode.OK, "Role:Anonymous:Value")]
        [InlineData("role/any", HttpStatusCode.Unauthorized)]
        [InlineData("role/role", HttpStatusCode.Unauthorized)]
        [Theory]
        public async Task NoUser(string route, HttpStatusCode expectedCode, string body = null)
        {
            using HttpClient client = CreateHttpClient();
            using HttpResponseMessage response = await client.GetAsync($"https://example.test/test-auth/{route}");
            Assert.Equal(expectedCode, response.StatusCode);
            if (body != null)
            {
                Assert.Equal(body, await response.Content.ReadAsStringAsync());
            }
        }

        [InlineData("no/no", HttpStatusCode.OK, "No:No:Value")]
        [InlineData("no/anonymous", HttpStatusCode.OK, "No:Anonymous:Value")]
        [InlineData("no/any", HttpStatusCode.OK, "No:Any:Value")]
        [InlineData("no/role", HttpStatusCode.Forbidden)]
        [InlineData("anonymous/no", HttpStatusCode.OK, "Anonymous:No:Value")]
        [InlineData("anonymous/anonymous", HttpStatusCode.OK, "Anonymous:Anonymous:Value")]
        [InlineData("anonymous/any", HttpStatusCode.OK, "Anonymous:Any:Value")]
        [InlineData("anonymous/role", HttpStatusCode.OK, "Anonymous:Role:Value")]
        [InlineData("any/no", HttpStatusCode.OK, "Any:No:Value")]
        [InlineData("any/anonymous", HttpStatusCode.OK, "Any:Anonymous:Value")]
        [InlineData("any/any", HttpStatusCode.OK, "Any:Any:Value")]
        [InlineData("any/role", HttpStatusCode.Forbidden)]
        [InlineData("role/no", HttpStatusCode.Forbidden)]
        [InlineData("role/anonymous", HttpStatusCode.OK, "Role:Anonymous:Value")]
        [InlineData("role/any", HttpStatusCode.Forbidden)]
        [InlineData("role/role", HttpStatusCode.Forbidden)]
        [Theory]
        public async Task UserBadRole(string route, HttpStatusCode expectedCode, string body = null)
        {
            using HttpClient client = CreateHttpClient("TestUser", "BadRole");
            using HttpResponseMessage response = await client.GetAsync($"https://example.test/test-auth/{route}");
            Assert.Equal(expectedCode, response.StatusCode);
            if (body != null)
            {
                Assert.Equal(body, await response.Content.ReadAsStringAsync());
            }
        }

        [InlineData("no/no", HttpStatusCode.OK, "No:No:Value")]
        [InlineData("no/anonymous", HttpStatusCode.OK, "No:Anonymous:Value")]
        [InlineData("no/any", HttpStatusCode.OK, "No:Any:Value")]
        [InlineData("no/role", HttpStatusCode.Forbidden)]
        [InlineData("anonymous/no", HttpStatusCode.OK, "Anonymous:No:Value")]
        [InlineData("anonymous/anonymous", HttpStatusCode.OK, "Anonymous:Anonymous:Value")]
        [InlineData("anonymous/any", HttpStatusCode.OK, "Anonymous:Any:Value")]
        [InlineData("anonymous/role", HttpStatusCode.OK, "Anonymous:Role:Value")]
        [InlineData("any/no", HttpStatusCode.OK, "Any:No:Value")]
        [InlineData("any/anonymous", HttpStatusCode.OK, "Any:Anonymous:Value")]
        [InlineData("any/any", HttpStatusCode.OK, "Any:Any:Value")]
        [InlineData("any/role", HttpStatusCode.Forbidden)]
        [InlineData("role/no", HttpStatusCode.OK, "Role:No:Value")]
        [InlineData("role/anonymous", HttpStatusCode.OK, "Role:Anonymous:Value")]
        [InlineData("role/any", HttpStatusCode.OK, "Role:Any:Value")]
        [InlineData("role/role", HttpStatusCode.Forbidden)]
        [Theory]
        public async Task UserControllerRole(string route, HttpStatusCode expectedCode, string body = null)
        {
            using HttpClient client = CreateHttpClient("TestUser", "ControllerRole");
            using HttpResponseMessage response = await client.GetAsync($"https://example.test/test-auth/{route}");
            Assert.Equal(expectedCode, response.StatusCode);
            if (body != null)
            {
                Assert.Equal(body, await response.Content.ReadAsStringAsync());
            }
        }

        [InlineData("no/no", HttpStatusCode.OK, "No:No:Value")]
        [InlineData("no/anonymous", HttpStatusCode.OK, "No:Anonymous:Value")]
        [InlineData("no/any", HttpStatusCode.OK, "No:Any:Value")]
        [InlineData("no/role", HttpStatusCode.OK, "No:Role:Value")]
        [InlineData("anonymous/no", HttpStatusCode.OK, "Anonymous:No:Value")]
        [InlineData("anonymous/anonymous", HttpStatusCode.OK, "Anonymous:Anonymous:Value")]
        [InlineData("anonymous/any", HttpStatusCode.OK, "Anonymous:Any:Value")]
        [InlineData("anonymous/role", HttpStatusCode.OK, "Anonymous:Role:Value")]
        [InlineData("any/no", HttpStatusCode.OK, "Any:No:Value")]
        [InlineData("any/anonymous", HttpStatusCode.OK, "Any:Anonymous:Value")]
        [InlineData("any/any", HttpStatusCode.OK, "Any:Any:Value")]
        [InlineData("any/role", HttpStatusCode.OK, "Any:Role:Value")]
        [InlineData("role/no", HttpStatusCode.Forbidden)]
        [InlineData("role/anonymous", HttpStatusCode.OK, "Role:Anonymous:Value")]
        [InlineData("role/any", HttpStatusCode.Forbidden)]
        [InlineData("role/role", HttpStatusCode.Forbidden)]
        [Theory]
        public async Task UserActionRole(string route, HttpStatusCode expectedCode, string body = null)
        {
            using HttpClient client = CreateHttpClient("TestUser", "ActionRole");
            using HttpResponseMessage response = await client.GetAsync($"https://example.test/test-auth/{route}");
            Assert.Equal(expectedCode, response.StatusCode);
            if (body != null)
            {
                Assert.Equal(body, await response.Content.ReadAsStringAsync());
            }
        }

        [InlineData("no/no", HttpStatusCode.OK, "No:No:Value")]
        [InlineData("no/anonymous", HttpStatusCode.OK, "No:Anonymous:Value")]
        [InlineData("no/any", HttpStatusCode.OK, "No:Any:Value")]
        [InlineData("no/role", HttpStatusCode.OK, "No:Role:Value")]
        [InlineData("anonymous/no", HttpStatusCode.OK, "Anonymous:No:Value")]
        [InlineData("anonymous/anonymous", HttpStatusCode.OK, "Anonymous:Anonymous:Value")]
        [InlineData("anonymous/any", HttpStatusCode.OK, "Anonymous:Any:Value")]
        [InlineData("anonymous/role", HttpStatusCode.OK, "Anonymous:Role:Value")]
        [InlineData("any/no", HttpStatusCode.OK, "Any:No:Value")]
        [InlineData("any/anonymous", HttpStatusCode.OK, "Any:Anonymous:Value")]
        [InlineData("any/any", HttpStatusCode.OK, "Any:Any:Value")]
        [InlineData("any/role", HttpStatusCode.OK, "Any:Role:Value")]
        [InlineData("role/no", HttpStatusCode.OK, "Role:No:Value")]
        [InlineData("role/anonymous", HttpStatusCode.OK, "Role:Anonymous:Value")]
        [InlineData("role/any", HttpStatusCode.OK, "Role:Any:Value")]
        [InlineData("role/role", HttpStatusCode.OK, "Role:Role:Value")]
        [Theory]
        public async Task UserBothRole(string route, HttpStatusCode expectedCode, string body = null)
        {
            using HttpClient client = CreateHttpClient("TestUser", "ControllerRole;ActionRole");
            using HttpResponseMessage response = await client.GetAsync($"https://example.test/test-auth/{route}");
            Assert.Equal(expectedCode, response.StatusCode);
            if (body != null)
            {
                Assert.Equal(body, await response.Content.ReadAsStringAsync());
            }
        }

        private HttpClient CreateHttpClient(string user = null, string role = null)
        {
            var factory = new TestAppFactory(_output);
            factory.ConfigureServices(services =>
                {
                    services.AddControllers(o =>
                    {
                        o.Conventions.Add(new DefaultAuthorizeActionModelConvention(null));
                    });
                    services.AddAuthenticationCore(o => { o.DefaultScheme = "None"; });
                    services.AddSingleton<UrlEncoder, UrlTestEncoder>();
                    services.AddSingleton<ISystemClock, TestClock>();
                    var b = new AuthenticationBuilder(services);
                    b.AddScheme<AutoAuthenticationTestSchemeOptions, AutoAuthenticationTestScheme>("None",
                        o =>
                        {
                            if (user != null)
                            {
                                o.UserName = user;
                                o.Role = role;
                            }
                        });

                    services.AddAuthorization();
                    services.AddLogging();
                }
            );

            factory.ConfigureBuilder(app =>
                {
                    app.UseRouting();
                    app.UseAuthentication();
                    app.UseAuthorization();
                    app.UseEndpoints(e => e.MapControllers());
                }
            );

            return factory.CreateClient(new WebApplicationFactoryClientOptions
                {
                    BaseAddress = new Uri("https://example.test", UriKind.Absolute),
                    AllowAutoRedirect = false
                }
            );
        }
    }

    [UsedImplicitly(ImplicitUseKindFlags.InstantiatedNoFixedConstructorSignature)]
    public class AutoAuthenticationTestScheme : AuthenticationHandler<AutoAuthenticationTestSchemeOptions>
    {
        public AutoAuthenticationTestScheme(
            IOptionsMonitor<AutoAuthenticationTestSchemeOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder,
            ISystemClock clock) : base(options, logger, encoder, clock)
        {
        }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (Options.UserName == null)
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var identity = new ClaimsIdentity("None");
            identity.AddClaim(new Claim(ClaimTypes.Name, Options.UserName));
            if (Options.Role != null)
            {
                foreach (string role in Options.Role.Split(";"))
                {
                    identity.AddClaim(new Claim(ClaimTypes.Role, role));
                }
            }

            var principal = new ClaimsPrincipal(identity);

            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, "None")));
        }
    }

    public class AutoAuthenticationTestSchemeOptions : AuthenticationSchemeOptions
    {
        public string UserName { get; set; }
        public string Role { get; set; }
    }
}
