using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Internal.Helix.KnownIssues.Models;

namespace Microsoft.Internal.Helix.KnownIssues.Services
{
    public interface IKnownIssuesHistoryService
    {
        Task SaveKnownIssuesHistory(IEnumerable<KnownIssue> knownIssues, int id);
        Task<List<KnownIssueAnalysis>> GetKnownIssuesHistory(string issueRepo, long issueId, DateTimeOffset since, CancellationToken cancellationToken);
        Task SaveKnownIssueError(string issueRepo, long issueId, List<string> errorMessages, CancellationToken cancellationToken);
        Task<KnownIssueError> GetLatestKnownIssueError(string issueRepo, long issueId, CancellationToken cancellationToken);
        Task SaveBuildKnownIssueValidation(int buildId, string issueRepo, long issueId, List<string> errorMessages, CancellationToken cancellationToken);
        Task<List<KnownIssueAnalysis>> GetBuildKnownIssueValidatedRecords(string buildId, string issueRepo, long issueId, CancellationToken cancellationToken);
    }
}
