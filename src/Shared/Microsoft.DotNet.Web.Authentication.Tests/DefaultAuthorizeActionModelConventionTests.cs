using System;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using FluentAssertions;
using JetBrains.Annotations;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.DotNet.Internal.Testing.Utility;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.WebEncoders.Testing;
using NUnit.Framework;

namespace Microsoft.DotNet.Web.Authentication.Tests
{
    [TestFixture, Parallelizable(ParallelScope.All)]
    public class DefaultAuthorizeActionModelConventionTests
    {
        [TestCase("no/no", HttpStatusCode.Unauthorized)]
        [TestCase("no/anonymous", HttpStatusCode.OK, "No:Anonymous:Value")]
        [TestCase("no/any", HttpStatusCode.Unauthorized)]
        [TestCase("no/role", HttpStatusCode.Unauthorized)]
        [TestCase("anonymous/no", HttpStatusCode.OK, "Anonymous:No:Value")]
        [TestCase("anonymous/anonymous", HttpStatusCode.OK, "Anonymous:Anonymous:Value")]
        [TestCase("anonymous/any", HttpStatusCode.OK, "Anonymous:Any:Value")]
        [TestCase("anonymous/role", HttpStatusCode.OK, "Anonymous:Role:Value")]
        [TestCase("any/no", HttpStatusCode.Unauthorized)]
        [TestCase("any/anonymous", HttpStatusCode.OK, "Any:Anonymous:Value")]
        [TestCase("any/any", HttpStatusCode.Unauthorized)]
        [TestCase("any/role", HttpStatusCode.Unauthorized)]
        [TestCase("role/no", HttpStatusCode.Unauthorized)]
        [TestCase("role/anonymous", HttpStatusCode.OK, "Role:Anonymous:Value")]
        [TestCase("role/any", HttpStatusCode.Unauthorized)]
        [TestCase("role/role", HttpStatusCode.Unauthorized)]
        public async Task NoUser(string route, HttpStatusCode expectedCode, string body = null)
        {
            using HttpClient client = CreateHttpClient();
            using HttpResponseMessage response = await client.GetAsync($"https://example.test/test-auth/{route}");
            response.StatusCode.Should().Be(expectedCode);
            if (body != null)
            {
                (await response.Content.ReadAsStringAsync()).Should().Be(body);
            }
        }

        [TestCase("no/no", HttpStatusCode.OK, "No:No:Value")]
        [TestCase("no/anonymous", HttpStatusCode.OK, "No:Anonymous:Value")]
        [TestCase("no/any", HttpStatusCode.OK, "No:Any:Value")]
        [TestCase("no/role", HttpStatusCode.Forbidden)]
        [TestCase("anonymous/no", HttpStatusCode.OK, "Anonymous:No:Value")]
        [TestCase("anonymous/anonymous", HttpStatusCode.OK, "Anonymous:Anonymous:Value")]
        [TestCase("anonymous/any", HttpStatusCode.OK, "Anonymous:Any:Value")]
        [TestCase("anonymous/role", HttpStatusCode.OK, "Anonymous:Role:Value")]
        [TestCase("any/no", HttpStatusCode.OK, "Any:No:Value")]
        [TestCase("any/anonymous", HttpStatusCode.OK, "Any:Anonymous:Value")]
        [TestCase("any/any", HttpStatusCode.OK, "Any:Any:Value")]
        [TestCase("any/role", HttpStatusCode.Forbidden)]
        [TestCase("role/no", HttpStatusCode.Forbidden)]
        [TestCase("role/anonymous", HttpStatusCode.OK, "Role:Anonymous:Value")]
        [TestCase("role/any", HttpStatusCode.Forbidden)]
        [TestCase("role/role", HttpStatusCode.Forbidden)]
        public async Task UserBadRole(string route, HttpStatusCode expectedCode, string body = null)
        {
            using HttpClient client = CreateHttpClient("TestUser", "BadRole");
            using HttpResponseMessage response = await client.GetAsync($"https://example.test/test-auth/{route}");
            response.StatusCode.Should().Be(expectedCode);
            if (body != null)
            {
                (await response.Content.ReadAsStringAsync()).Should().Be(body);
            }
        }

        [TestCase("no/no", HttpStatusCode.OK, "No:No:Value")]
        [TestCase("no/anonymous", HttpStatusCode.OK, "No:Anonymous:Value")]
        [TestCase("no/any", HttpStatusCode.OK, "No:Any:Value")]
        [TestCase("no/role", HttpStatusCode.Forbidden)]
        [TestCase("anonymous/no", HttpStatusCode.OK, "Anonymous:No:Value")]
        [TestCase("anonymous/anonymous", HttpStatusCode.OK, "Anonymous:Anonymous:Value")]
        [TestCase("anonymous/any", HttpStatusCode.OK, "Anonymous:Any:Value")]
        [TestCase("anonymous/role", HttpStatusCode.OK, "Anonymous:Role:Value")]
        [TestCase("any/no", HttpStatusCode.OK, "Any:No:Value")]
        [TestCase("any/anonymous", HttpStatusCode.OK, "Any:Anonymous:Value")]
        [TestCase("any/any", HttpStatusCode.OK, "Any:Any:Value")]
        [TestCase("any/role", HttpStatusCode.Forbidden)]
        [TestCase("role/no", HttpStatusCode.OK, "Role:No:Value")]
        [TestCase("role/anonymous", HttpStatusCode.OK, "Role:Anonymous:Value")]
        [TestCase("role/any", HttpStatusCode.OK, "Role:Any:Value")]
        [TestCase("role/role", HttpStatusCode.Forbidden)]
        public async Task UserControllerRole(string route, HttpStatusCode expectedCode, string body = null)
        {
            using HttpClient client = CreateHttpClient("TestUser", "ControllerRole");
            using HttpResponseMessage response = await client.GetAsync($"https://example.test/test-auth/{route}");
            response.StatusCode.Should().Be(expectedCode);
            if (body != null)
            {
                (await response.Content.ReadAsStringAsync()).Should().Be(body);
            }
        }

        [TestCase("no/no", HttpStatusCode.OK, "No:No:Value")]
        [TestCase("no/anonymous", HttpStatusCode.OK, "No:Anonymous:Value")]
        [TestCase("no/any", HttpStatusCode.OK, "No:Any:Value")]
        [TestCase("no/role", HttpStatusCode.OK, "No:Role:Value")]
        [TestCase("anonymous/no", HttpStatusCode.OK, "Anonymous:No:Value")]
        [TestCase("anonymous/anonymous", HttpStatusCode.OK, "Anonymous:Anonymous:Value")]
        [TestCase("anonymous/any", HttpStatusCode.OK, "Anonymous:Any:Value")]
        [TestCase("anonymous/role", HttpStatusCode.OK, "Anonymous:Role:Value")]
        [TestCase("any/no", HttpStatusCode.OK, "Any:No:Value")]
        [TestCase("any/anonymous", HttpStatusCode.OK, "Any:Anonymous:Value")]
        [TestCase("any/any", HttpStatusCode.OK, "Any:Any:Value")]
        [TestCase("any/role", HttpStatusCode.OK, "Any:Role:Value")]
        [TestCase("role/no", HttpStatusCode.Forbidden)]
        [TestCase("role/anonymous", HttpStatusCode.OK, "Role:Anonymous:Value")]
        [TestCase("role/any", HttpStatusCode.Forbidden)]
        [TestCase("role/role", HttpStatusCode.Forbidden)]
        public async Task UserActionRole(string route, HttpStatusCode expectedCode, string body = null)
        {
            using HttpClient client = CreateHttpClient("TestUser", "ActionRole");
            using HttpResponseMessage response = await client.GetAsync($"https://example.test/test-auth/{route}");
            response.StatusCode.Should().Be(expectedCode);
            if (body != null)
            {
                (await response.Content.ReadAsStringAsync()).Should().Be(body);
            }
        }

        [TestCase("no/no", HttpStatusCode.OK, "No:No:Value")]
        [TestCase("no/anonymous", HttpStatusCode.OK, "No:Anonymous:Value")]
        [TestCase("no/any", HttpStatusCode.OK, "No:Any:Value")]
        [TestCase("no/role", HttpStatusCode.OK, "No:Role:Value")]
        [TestCase("anonymous/no", HttpStatusCode.OK, "Anonymous:No:Value")]
        [TestCase("anonymous/anonymous", HttpStatusCode.OK, "Anonymous:Anonymous:Value")]
        [TestCase("anonymous/any", HttpStatusCode.OK, "Anonymous:Any:Value")]
        [TestCase("anonymous/role", HttpStatusCode.OK, "Anonymous:Role:Value")]
        [TestCase("any/no", HttpStatusCode.OK, "Any:No:Value")]
        [TestCase("any/anonymous", HttpStatusCode.OK, "Any:Anonymous:Value")]
        [TestCase("any/any", HttpStatusCode.OK, "Any:Any:Value")]
        [TestCase("any/role", HttpStatusCode.OK, "Any:Role:Value")]
        [TestCase("role/no", HttpStatusCode.OK, "Role:No:Value")]
        [TestCase("role/anonymous", HttpStatusCode.OK, "Role:Anonymous:Value")]
        [TestCase("role/any", HttpStatusCode.OK, "Role:Any:Value")]
        [TestCase("role/role", HttpStatusCode.OK, "Role:Role:Value")]
        public async Task UserBothRole(string route, HttpStatusCode expectedCode, string body = null)
        {
            using HttpClient client = CreateHttpClient("TestUser", "ControllerRole;ActionRole");
            using HttpResponseMessage response = await client.GetAsync($"https://example.test/test-auth/{route}");
            response.StatusCode.Should().Be(expectedCode);
            if (body != null)
            {
                (await response.Content.ReadAsStringAsync()).Should().Be(body);
            }
        }

        private HttpClient CreateHttpClient(string user = null, string role = null)
        {
            var factory = new TestAppFactory();
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
