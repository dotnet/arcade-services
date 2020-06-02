using System.Net.Http;
using Octokit;

namespace Microsoft.DotNet.GitHub.Authentication
{
    public interface IGitHubClientFactory
    {
        IGitHubClient CreateGitHubClient(string token);
    }
}
