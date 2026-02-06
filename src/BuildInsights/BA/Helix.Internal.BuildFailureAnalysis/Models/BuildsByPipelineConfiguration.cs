using System.Collections.Immutable;

namespace Microsoft.Internal.Helix.BuildFailureAnalysis.Models;

public class BuildsByPipelineConfiguration
{
    public ImmutableList<BuildReferenceIdentifier> IncludedPipelinesBuilds { get; }
    public ImmutableList<BuildReferenceIdentifier> FilteredPipelinesBuilds { get; }

    public BuildsByPipelineConfiguration(ImmutableList<BuildReferenceIdentifier> requestedBuilds,
        ImmutableList<BuildReferenceIdentifier> skippedFilteredPipelinesBuilds)
    {
        IncludedPipelinesBuilds = requestedBuilds;
        FilteredPipelinesBuilds = skippedFilteredPipelinesBuilds;
    }
}
