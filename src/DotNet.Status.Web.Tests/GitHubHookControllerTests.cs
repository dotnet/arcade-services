using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using DotNet.Status.Web.Controllers;
using DotNet.Status.Web.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.WebHooks.Filters;
using Microsoft.DotNet.GitHub.Authentication;
using Microsoft.DotNet.Internal.Testing.Utility;
using Microsoft.DotNet.Web.Authentication.Tests;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Moq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace DotNet.Status.Web.Tests
{
    public class TestVerifySignatureFilter : GitHubVerifySignatureFilter, IAsyncResourceFilter
    {
#pragma warning disable 618
        public TestVerifySignatureFilter(IConfiguration configuration, IHostingEnvironment hostingEnvironment, ILoggerFactory loggerFactory) : base(configuration, hostingEnvironment, loggerFactory)
#pragma warning restore 618
        {
        }

        public new async Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
        {
            await next();
        }
    }

    [TestFixture]
    public class GitHubHookControllerTests
    {
        public static string TestTeamsWebHookUri = "https://example.teams/webhook/sha";
        public static string WatchedTeam = "test-user/watched-team";
        public static string IgnoredRepo = "test-user/ignored";

        [Test]
        public async Task NewIssueWithMentionNotifies()
        {
            var data = new JObject
            {
                ["action"] = "opened",
                ["repository"] = new JObject
                {
                    ["owner"] = new JObject
                    {
                        ["login"] = "test-user",
                    },
                    ["name"] = "test",
                },
                ["issue"] = new JObject
                {
                    ["number"] = 2,
                    ["body"] = $"Something pizza @{WatchedTeam}",
                    ["user"] = new JObject
                    {
                        ["login"] = "thatguy",
                    },
                },
            };
            var eventName = "issues";
            await SendWebHook(data, eventName, true);
        }

        [Test]
        public async Task NewIssueWithoutMentionDoesntNotify()
        {
            var data = new JObject
            {
                ["action"] = "opened",
                ["repository"] = new JObject
                {
                    ["owner"] = new JObject
                    {
                        ["login"] = "test-user",
                    },
                    ["name"] = "test",
                },
                ["issue"] = new JObject
                {
                    ["number"] = 2,
                    ["body"] = "Something pizza",
                    ["user"] = new JObject
                    {
                        ["login"] = "thatguy",
                    },
                },
            };
            var eventName = "issues";
            await SendWebHook(data, eventName, false);
        }

        [Test]
        public async Task EditedIssueWithNewMentionNotifies()
        {
            var data = new JObject
            {
                ["action"] = "edited",
                ["repository"] = new JObject
                {
                    ["owner"] = new JObject
                    {
                        ["login"] = "test-user",
                    },
                    ["name"] = "test",
                },
                ["issue"] = new JObject
                {
                    ["number"] = 2,
                    ["body"] = $"Something pizza @{WatchedTeam}",
                    ["user"] = new JObject
                    {
                        ["login"] = "thatguy",
                    },
                },
                ["changes"] = new JObject
                {
                    ["body"] = new JObject
                    {
                        ["from"] = "Something pizza",
                    },
                },
            };
            var eventName = "issues";
            await SendWebHook(data, eventName, true);
        }

        [Test]
        public async Task EditedIssueWithExistingMentionDoesntNotify()
        {
            var data = new JObject
            {
                ["action"] = "edited",
                ["repository"] = new JObject
                {
                    ["owner"] = new JObject
                    {
                        ["login"] = "test-user",
                    },
                    ["name"] = "test",
                },
                ["issue"] = new JObject
                {
                    ["number"] = 2,
                    ["body"] = $"Something pizza @{WatchedTeam}",
                    ["user"] = new JObject
                    {
                        ["login"] = "thatguy",
                    },
                },
                ["changes"] = new JObject
                {
                    ["body"] = new JObject
                    {
                        ["from"] = $"Something @{WatchedTeam} pizza",
                    },
                },
            };
            var eventName = "issues";
            await SendWebHook(data, eventName, false);
        }

        [Test]
        public async Task EditedIssueWithRemovedMentionDoesntNotify()
        {
            var data = new JObject
            {
                ["action"] = "edited",
                ["repository"] = new JObject
                {
                    ["owner"] = new JObject
                    {
                        ["login"] = "test-user",
                    },
                    ["name"] = "test",
                },
                ["issue"] = new JObject
                {
                    ["number"] = 2,
                    ["body"] = "Something pizza",
                    ["user"] = new JObject
                    {
                        ["login"] = "thatguy",
                    },
                },
                ["changes"] = new JObject
                {
                    ["body"] = new JObject
                    {
                        ["from"] = $"Something @{WatchedTeam} pizza",
                    },
                },
            };
            var eventName = "issues";
            await SendWebHook(data, eventName, false);
        }

        [Test]
        public async Task EditedIssueWithNoMentionDoesntNotify()
        {
            var data = new JObject
            {
                ["action"] = "edited",
                ["repository"] = new JObject
                {
                    ["owner"] = new JObject
                    {
                        ["login"] = "test-user",
                    },
                    ["name"] = "test",
                },
                ["issue"] = new JObject
                {
                    ["number"] = 2,
                    ["body"] = "Something pizza",
                    ["user"] = new JObject
                    {
                        ["login"] = "thatguy",
                    },
                },
                ["changes"] = new JObject
                {
                    ["body"] = new JObject
                    {
                        ["from"] = "Something pineapple pizza",
                    },
                },
            };
            var eventName = "issues";
            await SendWebHook(data, eventName, false);
        }

        [Test]
        public async Task NewPullRequestWithMentionNotifies()
        {
            var data = new JObject
            {
                ["action"] = "opened",
                ["repository"] = new JObject
                {
                    ["owner"] = new JObject
                    {
                        ["login"] = "test-user",
                    },
                    ["name"] = "test",
                },
                ["pull_request"] = new JObject
                {
                    ["number"] = 2,
                    ["body"] = $"Something pizza @{WatchedTeam}",
                    ["user"] = new JObject
                    {
                        ["login"] = "thatguy",
                    },
                },
            };
            var eventName = "pull_request";
            await SendWebHook(data, eventName, true);
        }

        [Test]
        public async Task NewPullRequestWithoutMentionDoesntNotify()
        {
            var data = new JObject
            {
                ["action"] = "opened",
                ["repository"] = new JObject
                {
                    ["owner"] = new JObject
                    {
                        ["login"] = "test-user",
                    },
                    ["name"] = "test",
                },
                ["pull_request"] = new JObject
                {
                    ["number"] = 2,
                    ["body"] = "Something pizza",
                    ["user"] = new JObject
                    {
                        ["login"] = "thatguy",
                    },
                },
            };
            var eventName = "pull_request";
            await SendWebHook(data, eventName, false);
        }

        [Test]
        public async Task EditedPullRequestWithNewMentionNotifies()
        {
            var data = new JObject
            {
                ["action"] = "edited",
                ["repository"] = new JObject
                {
                    ["owner"] = new JObject
                    {
                        ["login"] = "test-user",
                    },
                    ["name"] = "test",
                },
                ["pull_request"] = new JObject
                {
                    ["number"] = 2,
                    ["body"] = $"Something pizza @{WatchedTeam}",
                    ["user"] = new JObject
                    {
                        ["login"] = "thatguy",
                    },
                },
                ["changes"] = new JObject
                {
                    ["body"] = new JObject
                    {
                        ["from"] = "Something pizza",
                    },
                },
            };
            var eventName = "pull_request";
            await SendWebHook(data, eventName, true);
        }

        [Test]
        public async Task EditedPullRequestWithExistingMentionDoesntNotify()
        {
            var data = new JObject
            {
                ["action"] = "edited",
                ["repository"] = new JObject
                {
                    ["owner"] = new JObject
                    {
                        ["login"] = "test-user",
                    },
                    ["name"] = "test",
                },
                ["pull_request"] = new JObject
                {
                    ["number"] = 2,
                    ["body"] = $"Something pizza @{WatchedTeam}",
                    ["user"] = new JObject
                    {
                        ["login"] = "thatguy",
                    },
                },
                ["changes"] = new JObject
                {
                    ["body"] = new JObject
                    {
                        ["from"] = $"Something @{WatchedTeam} pizza",
                    },
                },
            };
            var eventName = "pull_request";
            await SendWebHook(data, eventName, false);
        }

        [Test]
        public async Task EditedPullRequestWithRemovedMentionDoesntNotify()
        {
            var data = new JObject
            {
                ["action"] = "edited",
                ["repository"] = new JObject
                {
                    ["owner"] = new JObject
                    {
                        ["login"] = "test-user",
                    },
                    ["name"] = "test",
                },
                ["pull_request"] = new JObject
                {
                    ["number"] = 2,
                    ["body"] = $"Something pizza",
                    ["user"] = new JObject
                    {
                        ["login"] = "thatguy",
                    },
                },
                ["changes"] = new JObject
                {
                    ["body"] = new JObject
                    {
                        ["from"] = $"Something @{WatchedTeam} pizza",
                    },
                },
            };
            var eventName = "pull_request";
            await SendWebHook(data, eventName, false);
        }

        [Test]
        public async Task EditedPullRequestWithNoMentionDoesntNotify()
        {
            var data = new JObject
            {
                ["action"] = "edited",
                ["repository"] = new JObject
                {
                    ["owner"] = new JObject
                    {
                        ["login"] = "test-user",
                    },
                    ["name"] = "test",
                },
                ["pull_request"] = new JObject
                {
                    ["number"] = 2,
                    ["body"] = $"Something pizza",
                    ["user"] = new JObject
                    {
                        ["login"] = "thatguy",
                    },
                },
                ["changes"] = new JObject
                {
                    ["body"] = new JObject
                    {
                        ["from"] = "Something pineapple pizza",
                    },
                },
            };
            var eventName = "pull_request";
            await SendWebHook(data, eventName, false);
        }

        [Test]
        public async Task NewIssueCommentWithMentionNotifies()
        {
            var data = new JObject
            {
                ["action"] = "created",
                ["repository"] = new JObject
                {
                    ["owner"] = new JObject
                    {
                        ["login"] = "test-user",
                    },
                    ["name"] = "test",
                },
                ["issue"] = new JObject
                {
                    ["number"] = 2,
                },
                ["comment"] = new JObject
                {
                    ["user"] = new JObject
                    {
                        ["login"] = "thatguy",
                    },
                    ["body"] = $"Something pizza @{WatchedTeam}",
                },
            };
            var eventName = "issue_comment";
            await SendWebHook(data, eventName, true);
        }

        [Test]
        public async Task NewIssueCommentWithoutMentionDoesntNotify()
        {
            var data = new JObject
            {
                ["action"] = "created",
                ["repository"] = new JObject
                {
                    ["owner"] = new JObject
                    {
                        ["login"] = "test-user",
                    },
                    ["name"] = "test",
                },
                ["issue"] = new JObject
                {
                    ["number"] = 2,
                },
                ["comment"] = new JObject
                {
                    ["user"] = new JObject
                    {
                        ["login"] = "thatguy",
                    },
                    ["body"] = $"Something pizza",
                },
            };
            var eventName = "issue_comment";
            await SendWebHook(data, eventName, false);
        }

        [Test]
        public async Task EditedIssueCommentWithNewMentionNotifies()
        {
            var data = new JObject
            {
                ["action"] = "edited",
                ["repository"] = new JObject
                {
                    ["owner"] = new JObject
                    {
                        ["login"] = "test-user",
                    },
                    ["name"] = "test",
                },
                ["issue"] = new JObject
                {
                    ["number"] = 2,
                },
                ["comment"] = new JObject
                {
                    ["user"] = new JObject
                    {
                        ["login"] = "thatguy",
                    },
                    ["body"] = $"Something pizza @{WatchedTeam}",
                },
                ["changes"] = new JObject
                {
                    ["body"] = new JObject
                    {
                        ["from"] = "Something pizza",
                    },
                },
            };
            var eventName = "issue_comment";
            await SendWebHook(data, eventName, true);
        }

        [Test]
        public async Task EditedIssueCommentWithExistingMentionDoesntNotify()
        {
            var data = new JObject
            {
                ["action"] = "edited",
                ["repository"] = new JObject
                {
                    ["owner"] = new JObject
                    {
                        ["login"] = "test-user",
                    },
                    ["name"] = "test",
                },
                ["issue"] = new JObject
                {
                    ["number"] = 2,
                },
                ["comment"] = new JObject
                {
                    ["user"] = new JObject
                    {
                        ["login"] = "thatguy",
                    },
                    ["body"] = $"Something pizza @{WatchedTeam}",
                },
                ["changes"] = new JObject
                {
                    ["body"] = new JObject
                    {
                        ["from"] = $"Something pizza @{WatchedTeam}",
                    },
                },
            };
            var eventName = "issue_comment";
            await SendWebHook(data, eventName, false);
        }

        [Test]
        public async Task EditedIssueCommentWithRemovedMentionDoesntNotify()
        {
            var data = new JObject
            {
                ["action"] = "edited",
                ["repository"] = new JObject
                {
                    ["owner"] = new JObject
                    {
                        ["login"] = "test-user",
                    },
                    ["name"] = "test",
                },
                ["issue"] = new JObject
                {
                    ["number"] = 2,
                },
                ["comment"] = new JObject
                {
                    ["user"] = new JObject
                    {
                        ["login"] = "thatguy",
                    },
                    ["body"] = $"Something pizza",
                },
                ["changes"] = new JObject
                {
                    ["body"] = new JObject
                    {
                        ["from"] = $"Something pizza @{WatchedTeam}",
                    },
                },
            };
            var eventName = "issue_comment";
            await SendWebHook(data, eventName, false);
        }

        [Test]
        public async Task EditedIssueCommentWithNoMentionDoesntNotify()
        {
            var data = new JObject
            {
                ["action"] = "edited",
                ["repository"] = new JObject
                {
                    ["owner"] = new JObject
                    {
                        ["login"] = "test-user",
                    },
                    ["name"] = "test",
                },
                ["issue"] = new JObject
                {
                    ["number"] = 2,
                },
                ["comment"] = new JObject
                {
                    ["user"] = new JObject
                    {
                        ["login"] = "thatguy",
                    },
                    ["body"] = "Something pizza",
                },
                ["changes"] = new JObject
                {
                    ["body"] = new JObject
                    {
                        ["from"] = $"Something pizza",
                    },
                },
            };
            var eventName = "issue_comment";
            await SendWebHook(data, eventName, false);
        }

        [Test]
        public async Task NewPullRequestReviewCommentWithMentionNotifies()
        {
            var data = new JObject
            {
                ["action"] = "created",
                ["repository"] = new JObject
                {
                    ["owner"] = new JObject
                    {
                        ["login"] = "test-user",
                    },
                    ["name"] = "test",
                },
                ["pull_request"] = new JObject
                {
                    ["number"] = 2,
                },
                ["comment"] = new JObject
                {
                    ["user"] = new JObject
                    {
                        ["login"] = "thatguy",
                    },
                    ["body"] = $"Something pizza @{WatchedTeam}",
                },
            };
            var eventName = "pull_request_review_comment";
            await SendWebHook(data, eventName, true);
        }

        [Test]
        public async Task NewPullRequestReviewCommentWithoutMentionDoesntNotify()
        {
            var data = new JObject
            {
                ["action"] = "created",
                ["repository"] = new JObject
                {
                    ["owner"] = new JObject
                    {
                        ["login"] = "test-user",
                    },
                    ["name"] = "test",
                },
                ["pull_request"] = new JObject
                {
                    ["number"] = 2,
                },
                ["comment"] = new JObject
                {
                    ["user"] = new JObject
                    {
                        ["login"] = "thatguy",
                    },
                    ["body"] = $"Something pizza",
                },
            };
            var eventName = "pull_request_review_comment";
            await SendWebHook(data, eventName, false);
        }

        [Test]
        public async Task EditedPullRequestReviewCommentWithNewMentionNotifies()
        {
            var data = new JObject
            {
                ["action"] = "edited",
                ["repository"] = new JObject
                {
                    ["owner"] = new JObject
                    {
                        ["login"] = "test-user",
                    },
                    ["name"] = "test",
                },
                ["pull_request"] = new JObject
                {
                    ["number"] = 2,
                },
                ["comment"] = new JObject
                {
                    ["user"] = new JObject
                    {
                        ["login"] = "thatguy",
                    },
                    ["body"] = $"Something pizza @{WatchedTeam}",
                },
                ["changes"] = new JObject
                {
                    ["body"] = new JObject
                    {
                        ["from"] = "Something pizza",
                    },
                },
            };
            var eventName = "pull_request_review_comment";
            await SendWebHook(data, eventName, true);
        }

        [Test]
        public async Task EditedPullRequestReviewCommentWithExistingMentionDoesntNotify()
        {
            var data = new JObject
            {
                ["action"] = "edited",
                ["repository"] = new JObject
                {
                    ["owner"] = new JObject
                    {
                        ["login"] = "test-user",
                    },
                    ["name"] = "test",
                },
                ["pull_request"] = new JObject
                {
                    ["number"] = 2,
                },
                ["comment"] = new JObject
                {
                    ["user"] = new JObject
                    {
                        ["login"] = "thatguy",
                    },
                    ["body"] = $"Something pizza @{WatchedTeam}",
                },
                ["changes"] = new JObject
                {
                    ["body"] = new JObject
                    {
                        ["from"] = $"Something pizza @{WatchedTeam}",
                    },
                },
            };
            var eventName = "pull_request_review_comment";
            await SendWebHook(data, eventName, false);
        }

        [Test]
        public async Task EditedPullRequestReviewCommentWithRemovedMentionDoesntNotify()
        {
            var data = new JObject
            {
                ["action"] = "edited",
                ["repository"] = new JObject
                {
                    ["owner"] = new JObject
                    {
                        ["login"] = "test-user",
                    },
                    ["name"] = "test",
                },
                ["pull_request"] = new JObject
                {
                    ["number"] = 2,
                },
                ["comment"] = new JObject
                {
                    ["user"] = new JObject
                    {
                        ["login"] = "thatguy",
                    },
                    ["body"] = $"Something pizza",
                },
                ["changes"] = new JObject
                {
                    ["body"] = new JObject
                    {
                        ["from"] = $"Something pizza @{WatchedTeam}",
                    },
                },
            };
            var eventName = "pull_request_review_comment";
            await SendWebHook(data, eventName, false);
        }

        [Test]
        public async Task EditedPullRequestReviewCommentWithNoMentionDoesntNotify()
        {
            var data = new JObject
            {
                ["action"] = "edited",
                ["repository"] = new JObject
                {
                    ["owner"] = new JObject
                    {
                        ["login"] = "test-user",
                    },
                    ["name"] = "test",
                },
                ["pull_request"] = new JObject
                {
                    ["number"] = 2,
                },
                ["comment"] = new JObject
                {
                    ["user"] = new JObject
                    {
                        ["login"] = "thatguy",
                    },
                    ["body"] = "Something pizza",
                },
                ["changes"] = new JObject
                {
                    ["body"] = new JObject
                    {
                        ["from"] = $"Something pizza",
                    },
                },
            };
            var eventName = "pull_request_review_comment";
            await SendWebHook(data, eventName, false);
        }

        private async Task SendWebHook(JObject data, string eventName, bool expectNotification)
        {
            using TestData testData = SetupTestData(expectNotification);
            var text = data.ToString();
            var request = new HttpRequestMessage(HttpMethod.Post, "/api/webhooks/incoming/github")
            {
                Content = new StringContent(data.ToString(), Encoding.UTF8)
                {
                    Headers =
                    {
                        ContentType = new MediaTypeHeaderValue("application/json"),
                        ContentLength = text.Length,
                    },
                },
                Headers =
                {
                    {"X-GitHub-Event", eventName},
                },
            };
            var response = await testData.Client.SendAsync(request);
            response.EnsureSuccessStatusCode();
            testData.VerifyAll();
        }

        public TestData SetupTestData(bool expectNotification)
        {
            var mockClientFactory = new MockHttpClientFactory();
            var factory = new TestAppFactory();
            factory.ConfigureServices(services =>
            {
                services.AddControllers()
                    .AddApplicationPart(typeof(GitHubHookController).Assembly)
                    .AddGitHubWebHooks();
                services.Configure<TeamMentionForwardingOptions>(o =>
                {
                    o.IgnoreRepos = new []{IgnoredRepo};
                    o.WatchedTeam = WatchedTeam;
                    o.TeamsWebHookUri = TestTeamsWebHookUri;
                });
                services.AddScoped<ITeamMentionForwarder, TeamMentionForwarder>();
                services.AddSingleton<Microsoft.Extensions.Internal.ISystemClock, TestClock>();
                services.AddLogging();
                services.AddSingleton<IHttpClientFactory>(mockClientFactory);

                services.AddSingleton(Mock.Of<IGitHubApplicationClientFactory>());
                services.AddSingleton(Mock.Of<ITimelineIssueTriage>());


                services.RemoveAll<GitHubVerifySignatureFilter>();
                services.AddSingleton<TestVerifySignatureFilter>();
                services.Configure<MvcOptions>(o =>
                {
                    o.Filters.Remove(o.Filters.OfType<ServiceFilterAttribute>()
                        .First(f => f.ServiceType == typeof(GitHubVerifySignatureFilter)));
                    o.Filters.AddService<TestVerifySignatureFilter>();
                });
            });
            factory.ConfigureBuilder(app =>
            {
                app.Use(async (context, next) =>
                {
                    await next();
                });
                app.UseRouting();
                app.UseEndpoints(e => e.MapControllers());
            });
            
            if (expectNotification)
            {
                mockClientFactory.AddCannedResponse(TestTeamsWebHookUri, null, HttpStatusCode.NoContent, null, HttpMethod.Post);
            }

            return new TestData(factory.CreateClient(new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri("https://example.test", UriKind.Absolute),
                AllowAutoRedirect = false,
            }), factory, mockClientFactory);
        }

        public class TestData : IDisposable
        {
            public TestData(HttpClient client, TestAppFactory factory, MockHttpClientFactory mockClientFactory)
            {
                Client = client;
                Factory = factory;
                MockClientFactory = mockClientFactory;
            }

            public HttpClient Client { get; }
            public TestAppFactory Factory { get; }
            public MockHttpClientFactory MockClientFactory { get; }

            public void VerifyAll()
            {
                MockClientFactory.VerifyAll();
            }

            public void Dispose()
            {
                Client?.Dispose();
                Factory?.Dispose();
            }
        }
    }
}
