using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.DotNet.Web.Authentication.AccessToken;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.WebEncoders.Testing;
using Moq;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.DotNet.Web.Authentication.Tests
{
    public class PersonalAccessTokenAuthenticationTests
    {
        private readonly ITestOutputHelper _output;

        public PersonalAccessTokenAuthenticationTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public async Task NoTokenRequiringAuthIsRejected()
        {
            using HttpClient client = CreateClient(out _);

            using HttpResponseMessage response = await client.GetAsync("https://example.test/test-auth/role/role");
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Fact]
        public async Task BadTokenLengthRequiringAuthIsRejected()
        {
            using HttpClient client = CreateClient(out _);

            using var request = new HttpRequestMessage(HttpMethod.Get, "https://example.test/test-auth/role/role");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "WRONG-LENGTH-TOKEN");
            using HttpResponseMessage response = await client.SendAsync(request);

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Fact]
        public async Task BadTokenRequiringAuthIsRejected()
        {
            using HttpClient client = CreateClient(out _, 10);

            var zeroTokenBytes = new byte[PersonalAccessTokenUtilities.CalculateTokenSizeForPasswordSize(10)];

            using var request = new HttpRequestMessage(HttpMethod.Get, "https://example.test/test-auth/role/role");
            request.Headers.Authorization =
                new AuthenticationHeaderValue("Bearer", Convert.ToBase64String(zeroTokenBytes));
            using HttpResponseMessage response = await client.SendAsync(request);

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Fact]
        public async Task WrongAuthSchemeFailes()
        {
            using HttpClient client = CreateClient(out _, 10);

            string token = PersonalAccessTokenUtilities.EncodeToken(UserId, GetPasswordBytesForToken(42, 10));

            using var request = new HttpRequestMessage(HttpMethod.Get, "https://example.test/pat/user-name");
            request.Headers.Authorization = new AuthenticationHeaderValue("NotBearer", token);
            using HttpResponseMessage response = await client.SendAsync(request);
            
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Fact]
        public async Task GoodTokenWorks()
        {
            using HttpClient client = CreateClient(out _, 10);

            string token = PersonalAccessTokenUtilities.EncodeToken(UserId, GetPasswordBytesForToken(42, 10));

            using var request = new HttpRequestMessage(HttpMethod.Get, "https://example.test/pat/user-name");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using HttpResponseMessage response = await client.SendAsync(request);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(GetUser(UserId).Name, await response.Content.ReadAsStringAsync());
        }

        [Fact]
        public async Task WrongPasswordFails()
        {
            using HttpClient client = CreateClient(out _, 10);

            string token = PersonalAccessTokenUtilities.EncodeToken(UserId, GetPasswordBytesForToken(-42, 10));

            using var request = new HttpRequestMessage(HttpMethod.Get, "https://example.test/pat/user-name");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using HttpResponseMessage response = await client.SendAsync(request);
            
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Fact]
        public async Task FailedPrincipalValidateFails()
        {
            using HttpClient client = CreateClient(out _, 10, validatedPrincipal: context =>
            {
                context.RejectPrincipal();
                return Task.CompletedTask;
            });

            string token = PersonalAccessTokenUtilities.EncodeToken(UserId, GetPasswordBytesForToken(42, 10));

            using var request = new HttpRequestMessage(HttpMethod.Get, "https://example.test/pat/user-name");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using HttpResponseMessage response = await client.SendAsync(request);
            
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Fact]
        public async Task ExceptionFails()
        {
            using HttpClient client = CreateClient(out _, 10, validatedPrincipal: context =>
            {
                throw new Exception("Test Exception");
            });

            string token = PersonalAccessTokenUtilities.EncodeToken(UserId, GetPasswordBytesForToken(42, 10));

            using var request = new HttpRequestMessage(HttpMethod.Get, "https://example.test/pat/user-name");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using HttpResponseMessage response = await client.SendAsync(request);
            
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Fact]
        public async Task ReplacedPrincipalIsRespected()
        {
            using HttpClient client = CreateClient(out _, 10, validatedPrincipal: context =>
            {
                context.ReplacePrincipal(
                    new ClaimsPrincipal(
                        new ClaimsIdentity(
                            new[]
                            {
                                new Claim(ClaimTypes.Name, "REPLACED-NAME")
                            },
                            "REPLACED-TYPE"
                        )
                    )
                );
                return Task.CompletedTask;
            });

            string token = PersonalAccessTokenUtilities.EncodeToken(UserId, GetPasswordBytesForToken(42, 10));

            using var request = new HttpRequestMessage(HttpMethod.Get, "https://example.test/pat/user-name");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using HttpResponseMessage response = await client.SendAsync(request);
            
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("REPLACED-NAME", await response.Content.ReadAsStringAsync());
        }

        [Fact]
        public async Task GoodTokenWithRenamedSchemeWorks()
        {
            using HttpClient client = CreateClient(out _, 10, "TestSchemeName");

            string token = PersonalAccessTokenUtilities.EncodeToken(UserId, GetPasswordBytesForToken(42, 10));

            using var request = new HttpRequestMessage(HttpMethod.Get, "https://example.test/pat/user-name");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using HttpResponseMessage response = await client.SendAsync(request);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(GetUser(UserId).Name, await response.Content.ReadAsStringAsync());
        }

        [Fact]
        public async Task PasswordFromWrongSizeFails()
        {
            using HttpClient client = CreateClient(out TestAppFactory factory);

            var pat = factory.Services.GetRequiredService<PersonalAccessTokenAuthenticationHandler<TestUser>>();
            string token = PersonalAccessTokenUtilities.EncodeToken(42, GetPasswordBytesForToken(42, 10));

            using var request = new HttpRequestMessage(HttpMethod.Get, "https://example.test/pat/user-name");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using HttpResponseMessage response = await client.SendAsync(request);

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Fact]
        public async Task CreatedPasswordWorks()
        {
            using HttpClient client = CreateClient(out _, 10);

            string token = await client.GetStringAsync($"https://example.test/pat/create-token/{UserId}");

            using var request = new HttpRequestMessage(HttpMethod.Get, "https://example.test/pat/user-name");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using HttpResponseMessage response = await client.SendAsync(request);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(GetUser(UserId).Name, await response.Content.ReadAsStringAsync());
        }

        [Fact]
        public async Task CreatedPasswordWithAlternateSchemeWorks()
        {
            using HttpClient client = CreateClient(out _, 10, schemeName: "SomeOtherScheme");

            string token = await client.GetStringAsync($"https://example.test/pat/create-token/{UserId}?scheme=SomeOtherScheme");

            using var request = new HttpRequestMessage(HttpMethod.Get, "https://example.test/pat/user-name");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using HttpResponseMessage response = await client.SendAsync(request);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(GetUser(UserId).Name, await response.Content.ReadAsStringAsync());
        }

        [Fact]
        public async Task CreatedPasswordWithWrongSchemeFails()
        {
            using HttpClient client = CreateClient(out _, 10);

            using HttpResponseMessage response = await client.GetAsync($"https://example.test/pat/create-token/{UserId}?scheme=UnregisteredScheme");
            
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal(nameof(InvalidOperationException), await response.Content.ReadAsStringAsync());
        }

        public const int UserId = 42;


        private static readonly ImmutableList<TestUser> s_knownUsers =
            ImmutableList.Create(
                new TestUser(UserId, "TestUserName")
            );

        public static TestUser GetUser(int id)
        {
            return s_knownUsers.FirstOrDefault(u => u.Id == id);
        }

        private HttpClient CreateClient(
            out TestAppFactory factory,
            int passwordSize = 17,
            string schemeName = null,
            Func<PersonalAccessTokenValidatePrincipalContext<TestUser>, Task> validatedPrincipal = null)
        {
            Dictionary<int, string> storedHashes = new Dictionary<int, string>();

            void ConfigureOptions(PersonalAccessTokenAuthenticationOptions<TestUser> o)
            {
                o.Events = new PersonalAccessTokenEvents<TestUser>
                {
                    OnGetTokenHash = context =>
                    {
                        TestUser user = GetUser(context.TokenId);
                        if (user != null)
                        {
                            string hash = storedHashes.GetValueOrDefault(
                                context.TokenId,
                                TestHasher.CalculateHash(user, GetPasswordForToken(context.TokenId, passwordSize))
                            );
                            context.Success(hash, user);
                        }

                        return Task.CompletedTask;
                    },
                    OnSetTokenHash = context =>
                    {
                        storedHashes.Add(context.User.Id, context.Hash);
                        return Task.FromResult(context.User.Id);
                    },
                };
                if (validatedPrincipal != null)
                    o.Events.OnValidatePrincipal = validatedPrincipal;
                o.PasswordSize = passwordSize;
            }

            void ConfigureAuth(AuthenticationBuilder b)
            {
                if (schemeName == null)
                {
                    b.AddPersonalAccessToken<TestUser>(ConfigureOptions);
                }
                else
                {
                    b.AddPersonalAccessToken<TestUser>(schemeName, $"Display {schemeName}", ConfigureOptions);
                }
            }

            var localClock = new TestClock();
            factory = new TestAppFactory(_output);
            factory.ConfigureServices(services =>
            {
                services.AddSingleton<ISystemClock>(localClock);
                services.AddSingleton<IPasswordHasher<TestUser>, TestHasher>();
                services.AddControllers();
                services.AddAuthenticationCore(o =>
                    o.DefaultScheme = schemeName ?? PersonalAccessTokenDefaults.AuthenticationScheme);
                ConfigureAuth(new AuthenticationBuilder(services));
                services.AddAuthorization();
                services.AddIdentityCore<TestUser>();
                services.AddSingleton<UrlEncoder, UrlTestEncoder>();
                services.AddSingleton<SignInManager<TestUser>>();
                var userStore = new Mock<IUserStore<TestUser>>();
                services.AddSingleton(userStore.Object);
                services.AddHttpContextAccessor();
                services.AddSingleton<IUserClaimsPrincipalFactory<TestUser>, TestClaimsFactory>();
            });

            factory.ConfigureBuilder(app =>
            {
                app.UseRouting();
                app.UseAuthentication();
                app.UseAuthorization();
                app.UseEndpoints(e => e.MapControllers());
            });

            HttpClient client = factory.CreateClient(new WebApplicationFactoryClientOptions
                {
                    BaseAddress = new Uri("https://example.test", UriKind.Absolute),
                    AllowAutoRedirect = false
                }
            );
            return client;
        }

        private static string GetPasswordForToken(int tokenId, int passwordSize)
        {
            return PersonalAccessTokenUtilities.EncodePasswordBytes(GetPasswordBytesForToken(tokenId, passwordSize));
        }

        private static byte[] GetPasswordBytesForToken(int tokenId, int passwordSize)
        {
            byte[] sizeBytes = BitConverter.GetBytes(passwordSize);
            using var sha256 = SHA256.Create();
            byte[] sizeHash = sha256.ComputeHash(sizeBytes);


            var passwordBytes = new byte[passwordSize];
            Array.Copy(sizeHash, passwordBytes, Math.Min(sizeHash.Length, passwordSize));
            byte[] idBytes = BitConverter.GetBytes(tokenId);
            Array.Copy(idBytes, passwordBytes, idBytes.Length);
            return passwordBytes;
        }
    }

    internal class TestClaimsFactory : IUserClaimsPrincipalFactory<TestUser>
    {
        public Task<ClaimsPrincipal> CreateAsync(TestUser user)
        {
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.Name, user.Name),
            };

            foreach (string role in user.Roles?.Split(';') ?? Enumerable.Empty<string>())
            {
                claims.Add(new Claim(ClaimTypes.Role, role));
            }

            var identity = new ClaimsIdentity(claims, "TestClaims");
            var claimsPrincipal = new ClaimsPrincipal(identity);
            return Task.FromResult(claimsPrincipal);
        }
    }

    public class TestHasher : IPasswordHasher<TestUser>
    {
        public string HashPassword(TestUser user, string password)
        {
            return CalculateHash(user, password);
        }

        public static string CalculateHash(TestUser user, string password)
        {
            return $":HASH:{user.Name}:{password}";
        }

        public PasswordVerificationResult VerifyHashedPassword(
            TestUser user,
            string hashedPassword,
            string providedPassword)
        {
            if (hashedPassword == CalculateHash(user, providedPassword))
            {
                return PasswordVerificationResult.Success;
            }

            return PasswordVerificationResult.Failed;
        }
    }

    public class TestClock : ISystemClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTime.Parse("2001-02-03T16:05:06Z");
    }

    public class TestUser
    {
        public TestUser(int id, string name, string roles = null)
        {
            Id = id;
            Name = name;
            Roles = roles;
        }

        public int Id { get; }
        public string Name { get; }
        public string Roles { get; }
    }

    [Route("pat")]
    public class PersonalAccessTokenController : ControllerBase
    {
        [Route("create-token/{id}")]
        [AllowAnonymous]
        public async Task<IActionResult> Create(int id, string scheme = null)
        {
            try
            {
                string token;
                if (scheme == null)
                {
                    (_, token) = await HttpContext.CreatePersonalAccessTokenAsync(
                        PersonalAccessTokenAuthenticationTests.GetUser(id),
                        "IGNORED");
                }
                else
                {
                    (_, token) = await HttpContext.CreatePersonalAccessTokenAsync(
                        scheme,
                        PersonalAccessTokenAuthenticationTests.GetUser(id),
                        "IGNORED");
                }

                return Ok(token);
            }
            catch (Exception e)
            {
                return BadRequest(e.GetType().Name);
            }
        }

        [Route("user-name")]
        [Authorize]
        public IActionResult UserName()
        {
            return Ok(User.Identity.Name);
        }
    }
}
