using Microsoft.Extensions.Options;
using Octokit;

namespace Microsoft.DotNet.GitHub.Authentication
{
    public class GitHubClientFactory : IGitHubClientFactory
    {
        private readonly IOptionsMonitor<GitHubClientOptions> _githubClientOptions;

        public GitHubClientFactory(IOptionsMonitor<GitHubClientOptions> githubClientOptions)
        {
            _githubClientOptions = githubClientOptions;
        }

        public GitHubClientOptions Options => _githubClientOptions.CurrentValue; 

        public IGitHubClient CreateGitHubClient(string token)
        {
            return new GitHubClient(Options.ProductHeader)
            {
                Credentials = new Credentials(token),
            };
        }
    }
}
