using System.Threading;
using System.Threading.Tasks;
using Microsoft.Internal.Helix.BuildFailureAnalysis.Models;

namespace Microsoft.Internal.Helix.BuildFailureAnalysis.Services
{
    public interface IBuildAnalysisService
    {
        public Task<BuildResultAnalysis> GetBuildResultAnalysisAsync(BuildReferenceIdentifier buildReference, CancellationToken cancellationToken, bool isValidationAnalysis = false);
    }
}
