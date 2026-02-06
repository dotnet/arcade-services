using System.Collections.Generic;
using System.Collections.Immutable;

namespace Microsoft.Internal.Helix.BuildFailureAnalysis.Models
{
    public class RelatedBuilds
    {
        public RelatedBuilds(ImmutableList<BuildReferenceIdentifier> relatedBuildsList)
        {
            RelatedBuildsList = relatedBuildsList;
        }

        public RelatedBuilds(IEnumerable<BuildReferenceIdentifier> relatedBuilds) : this(relatedBuilds.ToImmutableList())
        {
        }

        public ImmutableList<BuildReferenceIdentifier> RelatedBuildsList { get; }
    }
}
