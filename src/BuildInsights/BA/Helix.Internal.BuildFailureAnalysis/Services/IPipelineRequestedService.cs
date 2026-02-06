using System.Collections.Immutable;
using System.Threading.Tasks;
using Microsoft.Internal.Helix.BuildFailureAnalysis.Models;

namespace Microsoft.Internal.Helix.BuildFailureAnalysis.Services;

public interface IPipelineRequestedService
{
    Task<bool> IsBuildPipelineRequested(string repositoryId, string targetBranch, int definitionId, int buildId);
    Task<BuildsByPipelineConfiguration> GetBuildsByPipelineConfiguration(ImmutableList<BuildReferenceIdentifier> relatedBuilds, NamedBuildReference buildReference);
}
