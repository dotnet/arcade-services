using System;

namespace Microsoft.Internal.Helix.BuildFailureAnalysis.Models
{
    public class BuildReference
    {
        public BuildReference() { }

        public DateTimeOffset? Date { get; set; }

        /// <summary>
        /// In the form 20210226.40
        /// </summary>
        public string BuildNumber { get; set; }
        public string BuildLink { get; set; }
        public string CommitLink { get; set; }
        public string CommitHash { get; set; }
    }
}
