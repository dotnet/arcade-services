using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading.Tasks;
using Microsoft.Internal.Helix.KnownIssues.Models;
using Octokit;

namespace Microsoft.Internal.Helix.KnownIssues.Services
{
    public interface IGitHubIssuesService
    {
        Task<ImmutableList<KnownIssue>> GetCriticalInfrastructureIssuesAsync();
        Task<IEnumerable<KnownIssue>> GetInfrastructureKnownIssues();
        Task<IEnumerable<KnownIssue>> GetRepositoryKnownIssues(string buildRepo);
        Task UpdateIssueBodyAsync(string repository, int issueNumber, string description);
        Task<Issue> GetIssueAsync(string repository, int issueNumber);
        Task AddLabelToIssueAsync(string repository, int issueNumber, string label);
        Task AddCommentToIssueAsync(string repository, int issueNumber, string comment);
    }
}
