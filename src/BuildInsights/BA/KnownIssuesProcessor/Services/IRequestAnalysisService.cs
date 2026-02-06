using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Internal.Helix.BuildFailureAnalysis.Models;

namespace Microsoft.Internal.Helix.KnownIssuesProcessor.Services
{
    public interface IRequestAnalysisService
    {
        Task RequestAnalysisAsync(IReadOnlyList<Build> buildList);
    }
}
