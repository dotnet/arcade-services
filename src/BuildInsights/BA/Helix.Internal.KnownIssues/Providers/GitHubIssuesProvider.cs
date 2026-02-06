using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.DotNet.GitHub.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Internal.Helix.GitHub.Providers;
using Microsoft.Internal.Helix.KnownIssues.Models;
using Microsoft.Internal.Helix.KnownIssues.Services;
using Microsoft.Internal.Helix.Utility;
using Octokit;

namespace Microsoft.Internal.Helix.KnownIssues.Providers
{
    public class GitHubIssuesProvider : IGitHubIssuesService
    {
        private readonly IGitHubApplicationClientFactory _gitHubApplicationClientFactory;
        private readonly IEnumerable<string> _criticalIssuesLabels;
        private readonly IEnumerable<string> _criticalIssuesRepositories;
        private readonly IEnumerable<string> _knownIssuesLabels;
        private readonly IEnumerable<string> _knownIssuesRepositories;
        private readonly ILogger<GitHubIssuesProvider> _logger;

        public GitHubIssuesProvider(
            IGitHubApplicationClientFactory gitHubApplicationClientFactory,
            IOptions<GitHubIssuesSettings> gitHubIssuesSettings,
            ILogger<GitHubIssuesProvider> logger)
        {
            _gitHubApplicationClientFactory = gitHubApplicationClientFactory;
            _criticalIssuesLabels = gitHubIssuesSettings.Value.CriticalIssuesLabels;
            _criticalIssuesRepositories = gitHubIssuesSettings.Value.CriticalIssuesRepositories;
            _knownIssuesLabels = gitHubIssuesSettings.Value.KnownIssuesLabels;
            _knownIssuesRepositories = gitHubIssuesSettings.Value.KnownIssuesRepositories;
            _logger = logger;
        }

        public async Task<ImmutableList<KnownIssue>> GetIssues(string repository, KnownIssueType type, IEnumerable<string> labels = null)
        {
            try
            {
                (string owner, string name) = GithubRepositoryHelper.GetOwnerAndName(repository);
                IGitHubClient client = await _gitHubApplicationClientFactory.CreateGitHubClientAsync(owner, name);
                var criticalIssueFilter = new RepositoryIssueRequest
                {
                    Filter = IssueFilter.All
                };
                if (labels != null)
                {
                    foreach (var label in labels)
                        criticalIssueFilter.Labels.Add(label);
                }
                IReadOnlyList<Issue> issues = await client.Issue.GetAllForRepository(owner, name, criticalIssueFilter);

                List<KnownIssue> knownIssues = new List<KnownIssue>();
                foreach (Issue issue in issues)
                {
                    try
                    {
                        knownIssues.Add(KnownIssueHelper.ParseGithubIssue(issue, repository, type));
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning($"Unable to process issue: {repository}#{issue.Number} to get known issue. Error message:{ex.Message}");
                    }
                }

                return knownIssues.ToImmutableList();
            }
            catch (Exception ex) when (ex is ArgumentException)
            {
                _logger.LogInformation($"Unable to process repository: {repository} to get known issues");
                return ImmutableList<KnownIssue>.Empty;
            }
        }

        private async Task<List<KnownIssue>> GetIssuesWithOrLabels(string repository, KnownIssueType type,  IEnumerable<string> labels)
        {
            var knownIssues = new List<KnownIssue>();
            foreach (string knownIssuesLabel in labels)
            {
                knownIssues.AddRange(await GetIssues(repository, type, new List<string>() { knownIssuesLabel }));
            }

            return knownIssues;
        }

        public async Task<ImmutableList<KnownIssue>> GetCriticalInfrastructureIssuesAsync()
        {
            var criticalInfraIssues = new List<KnownIssue>();
            foreach (string repository in _criticalIssuesRepositories.Distinct())
            {
                criticalInfraIssues.AddRange(await GetIssues(repository, KnownIssueType.Critical, _criticalIssuesLabels));
            }

            return criticalInfraIssues.ToImmutableList();
        }

        public async Task<IEnumerable<KnownIssue>> GetRepositoryKnownIssues(string buildRepo)
        {
            KnownIssueType knownIssueType = _knownIssuesRepositories.Contains(buildRepo) ? KnownIssueType.Infrastructure : KnownIssueType.Repo;
            return await GetIssuesWithOrLabels(buildRepo, knownIssueType, _knownIssuesLabels);
        }

        public async Task<IEnumerable<KnownIssue>> GetInfrastructureKnownIssues()
        {
            var knownIssues = new List<KnownIssue>();
            foreach (string repository in _knownIssuesRepositories.Distinct())
            {
                knownIssues.AddRange(await GetIssuesWithOrLabels(repository, KnownIssueType.Infrastructure, _knownIssuesLabels));
            }

            return knownIssues;
        }

        public async Task UpdateIssueBodyAsync(string repository, int issueNumber, string description)
        {
            (string owner, string name) = GithubRepositoryHelper.GetOwnerAndName(repository);

            IGitHubClient client = await _gitHubApplicationClientFactory.CreateGitHubClientAsync(owner, name);

            Issue issue = await client.Issue.Get(owner, name, issueNumber);
            IssueUpdate issueUpdate = issue.ToUpdate();
            issueUpdate.Body = description;

            await client.Issue.Update(owner, name, issueNumber, issueUpdate);
        }

        public async Task<Issue> GetIssueAsync(string repository, int issueNumber)
        {
            (string owner, string name) = GithubRepositoryHelper.GetOwnerAndName(repository);

            IGitHubClient client = await _gitHubApplicationClientFactory.CreateGitHubClientAsync(owner, name);
            Issue issue = await client.Issue.Get(owner, name, issueNumber);
            return issue;
        }

        public async Task AddLabelToIssueAsync(string repository, int issueNumber, string label)
        {
            (string owner, string name) = repository.Split("/"); //RepositoryId is formed owner/name

            IGitHubClient client = await _gitHubApplicationClientFactory.CreateGitHubClientAsync(owner, name);
            await client.Issue.Labels.AddToIssue(owner, name, issueNumber, new[] {label});
        }

        public async Task AddCommentToIssueAsync(string repository, int issueNumber, string comment)
        {
            (string owner, string name) = GithubRepositoryHelper.GetOwnerAndName(repository);
            IGitHubClient client = await _gitHubApplicationClientFactory.CreateGitHubClientAsync(owner, name);

            try
            {
                await client.Issue.Comment.Create(owner, name, issueNumber, comment);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating comment on issue: {owner}/{repository}#{issueNumber}", owner, name, issueNumber);
                throw;
            }
        }

    }
}
