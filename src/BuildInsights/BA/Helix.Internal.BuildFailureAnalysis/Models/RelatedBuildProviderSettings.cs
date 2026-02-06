using System.Collections.Generic;

namespace Microsoft.Internal.Helix.BuildFailureAnalysis.Models
{
    public class RelatedBuildProviderSettings
    {
        public Dictionary<string, string> AllowedTargetProjects { get; set; }
    }
}
