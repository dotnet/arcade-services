using Microsoft.DotNet.Internal.AzureDevOps;
using System.Collections.Generic;

namespace Microsoft.DotNet.AzureDevOpsTimeline.Tests
{
    public class BuildAndTimeline
    {
        public Build Build { get; }
        public IList<Timeline> Timelines { get; } = new List<Timeline>();

        public BuildAndTimeline(Build build)
        {
            Build = build;
        }

        public BuildAndTimeline(Build build, IList<Timeline> timelines)
        {
            Build = build;
            Timelines = timelines;
        }
    }
}
