// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;

namespace DotNet.Status.Web
{
    public class ZenHubClient
    {
        private readonly IOptionsMonitor<ZenHubOptions> _options;
        private readonly ILogger<ZenHubClient> _logger;

        public ZenHubClient(IOptionsMonitor<ZenHubOptions> options, ILogger<ZenHubClient> logger)
        {
            _options = options;
            _logger = logger;
        }

        public async Task AddIssueToEpic(IssueIdentifier child, IssueIdentifier epic)
        {
            string apiToken = _options.CurrentValue.ApiToken;

            if (string.IsNullOrEmpty(apiToken))
            {
                _logger.LogWarning("No ZenHub API token configured, skipping");
            }

            using (var client = new HttpClient())
            {
                client.DefaultRequestHeaders.Add("X-Authentication-Token", apiToken);
                string epicUpdateUri = $"https://api.zenhub.com/p1/repositories/{epic.RepositoryId}/epics/{epic.IssueId}/update_issues";
                string body = JsonConvert.SerializeObject(new
                {
                    remove_issues = Array.Empty<string>(),
                    add_issues = new[]
                    {
                        new {repo_id = child.RepositoryId, issue_number = child.IssueId}
                    }
                });

                var content = new StringContent(body, Encoding.UTF8, "application/json");

                _logger.LogInformation("Moving issue {child_repo}#{child_issue} under epic {epic_repo}#{epic_issue}", child.RepositoryId, child.IssueId, epic.RepositoryId, epic.IssueId);
                using (HttpResponseMessage response = await client.PostAsync(epicUpdateUri, content))
                {
                    response.EnsureSuccessStatusCode();
                }
            }
        }

        public struct IssueIdentifier
        {
            public IssueIdentifier(long repositoryId, int issueId)
            {
                RepositoryId = repositoryId;
                IssueId = issueId;
            }

            public long RepositoryId { get; }
            public int IssueId { get; }
        }
    }
}
