// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using Newtonsoft.Json;

namespace ProductConstructionService.Client.Models
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
        public List<MergePolicy> MergePolicies { get; set; }
    }
}
