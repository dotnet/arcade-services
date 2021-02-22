using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using DotNet.Status.Web.TeamsMessages;
using JetBrains.Annotations;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;

namespace DotNet.Status.Web.Models
{
    public class IssueMentionForwardingOptions
    {
        public string[] WatchedTeams { get; set; }
        public string[] IgnoreRepos { get; set; }
        public string TeamsWebHookUri { get; set; }
    }

    public interface IIssueMentionForwarder
    {
        Task<bool> HandleIssueBody([CanBeNull] string oldBody, string newBody, string title, string commentUri, string username, DateTimeOffset date);
    }

    public class IssueMentionForwarder : IIssueMentionForwarder
    {
        private readonly IOptionsSnapshot<IssueMentionForwardingOptions> _options;
        private readonly ILogger<IssueMentionForwarder> _logger;
        private readonly IHttpClientFactory _httpClientFactory;

        public IssueMentionForwarder(IHttpClientFactory httpClientFactory, IOptionsSnapshot<IssueMentionForwardingOptions> options, ILogger<IssueMentionForwarder> logger)
        {
            _httpClientFactory = httpClientFactory;
            _options = options;
            _logger = logger;
        }

        public async Task<bool> HandleIssueBody([CanBeNull] string oldBody, string newBody, string title, string commentUri, string username, DateTimeOffset date)
        {
            var teams = _options.Value.WatchedTeams;
            var foundTeam = teams.FirstOrDefault(newBody.Contains);
            if (string.IsNullOrEmpty(foundTeam))
            {
                return false;
            }
            if (!string.IsNullOrEmpty(oldBody))
            {
                var oldTeam = teams.FirstOrDefault(oldBody.Contains);
                if (oldTeam == foundTeam)
                {
                    return false;
                }
            }

            await SendCommentNotification(title, newBody, foundTeam, commentUri, username, date);
            return true;
        }

        private async Task SendCommentNotification(string title, string body, string teamName, string commentUri, string username, DateTimeOffset date)
        {
            var message = new MessageCard
            {
                ThemeColor = "0072C6",
                Text = $"Team {teamName} was mentioned in an issue.",
                Actions = new List<IAction> {
                    new OpenUri {
                        Name = "Open comment",
                        Targets = new List<Target>
                        {
                            new Target
                            {
                                OperatingSystem = "default",
                                Uri = commentUri
                            }
                        }
                    }
                },
                Sections = new List<Section> {
                    new Section {
                        ActivityTitle = title,
                        ActivitySubtitle = $"{username} on {date:g}",
                        ActivityText = body
                    }
                }
            };
            HttpClient client = _httpClientFactory.CreateClient();
            using var req = new HttpRequestMessage(HttpMethod.Post, _options.Value.TeamsWebHookUri)
            {
                Content = new StringContent(JsonConvert.SerializeObject(message), Encoding.UTF8),
            };
            using var res = await client.SendAsync(req);
            res.EnsureSuccessStatusCode();
        }
    }
}
