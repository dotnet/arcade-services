using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading.Tasks;
using Microsoft.Internal.Helix.KnownIssues.Models;

namespace Microsoft.Internal.Helix.KnownIssues.Services
{
    public interface IKnownIssuesService
    {
        Task<ImmutableList<KnownIssueMatch>> GetKnownIssuesMatchesForIssue(int issueId, string issueRepository);
        Task<ImmutableList<TestKnownIssueMatch>> GetTestKnownIssuesMatchesForIssue(int issueId, string issueRepository);
        Task SaveKnownIssuesMatches(int buildId, List<KnownIssueMatch> knownIssueMatches);
        Task SaveTestsKnownIssuesMatches(int buildId, List<TestKnownIssueMatch> knownIssueMatches);
        Task SaveKnownIssuesHistory(IEnumerable<KnownIssue> knownIssues, int id);
    }
}
