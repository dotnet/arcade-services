using System;
using System.Collections.Immutable;
using Newtonsoft.Json;

namespace Microsoft.DotNet.Maestro.Client.Models
{
    public partial class RepositoryBranch
    {
        public RepositoryBranch()
        {
        }

        [JsonProperty("repository")]
        public string Repository { get; set; }

        [JsonProperty("branch")]
        public string Branch { get; set; }

        [JsonProperty("mergePolicies")]
        public IImmutableList<MergePolicy> MergePolicies { get; set; }
    }
}
