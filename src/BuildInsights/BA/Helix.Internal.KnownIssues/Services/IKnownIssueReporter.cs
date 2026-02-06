using System.Threading.Tasks;

namespace Microsoft.Internal.Helix.KnownIssues.Services
{
    public interface IKnownIssueReporter
    {
        Task ExecuteKnownIssueReporter();
    }
}
