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
        public string WatchedTeam { get; set; }
        public string[] IgnoreRepos { get; set; }
        public string TeamsWebHookUri { get; set; }
    }

    public interface ITeamMentionForwarder
    {
        Task HandleMentions(string repo, [CanBeNull] string oldBody, string newBody, string title, string commentUri, string username, DateTimeOffset date);
    }

    public class TeamMentionForwarder : ITeamMentionForwarder
    {
        private readonly IOptionsSnapshot<IssueMentionForwardingOptions> _options;
        private readonly ILogger<TeamMentionForwarder> _logger;
        private readonly IHttpClientFactory _httpClientFactory;

        public TeamMentionForwarder(IHttpClientFactory httpClientFactory, IOptionsSnapshot<IssueMentionForwardingOptions> options, ILogger<TeamMentionForwarder> logger)
        {
            _httpClientFactory = httpClientFactory;
            _options = options;
            _logger = logger;
        }

        public async Task HandleMentions(string repo, [CanBeNull] string oldBody, string newBody, string title, string commentUri, string username, DateTimeOffset date)
        {
            bool shouldSend = true;
            string team = _options.Value.WatchedTeam;
            string[] ignoredRepos = _options.Value.IgnoreRepos;

            if (ignoredRepos.Contains(repo))
            {
                shouldSend = false;
            }

            if (!newBody.Contains(team))
            {
                shouldSend = false;
            }
            else if (!string.IsNullOrEmpty(oldBody))
            {
                if (oldBody.Contains(team))
                {
                    shouldSend = false;
                }
            }

            if (shouldSend)
            {
                await SendCommentNotification(title, newBody, team, commentUri, username, date);
            }
        }

        private async Task SendCommentNotification(string title, string body, string teamName, string commentUri, string username, DateTimeOffset date)
        {
            try
            {
                _logger.LogInformation("Sending notification about mention of {teamName} at {uri}.", teamName, commentUri);
                var message = new MessageCard
                {
                    ThemeColor = "0072C6",
                    Text = $"Team {teamName} was mentioned in an issue.",
                    Actions = new List<IAction>
                    {
                        new OpenUri
                        {
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
                    Sections = new List<Section>
                    {
                        new Section
                        {
                            ActivityTitle = $"{username}",
                            ActivitySubtitle = "on {date:g}",
                            ActivityText = body
                        }
                    }
                };
                using HttpClient client = _httpClientFactory.CreateClient();
                using var req = new HttpRequestMessage(HttpMethod.Post, _options.Value.TeamsWebHookUri)
                {
                    Content = new StringContent(JsonConvert.SerializeObject(message), Encoding.UTF8),
                };
                using HttpResponseMessage res = await client.SendAsync(req);
                res.EnsureSuccessStatusCode();
                _logger.LogInformation("Sent notification about mention of {teamName} at {uri}.", teamName, commentUri);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Unable to send notification about mention of {teamName} at {uri}.", teamName, commentUri);
            }
        }
    }
}
