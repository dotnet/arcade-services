using System.Threading.Tasks;

namespace Microsoft.Internal.Helix.KnownIssues.Services
{
    public interface IKnownIssuesAnalysisService
    {
        Task RequestKnownIssuesAnalysis(string organization, string repository, long issueId);
    }
}
