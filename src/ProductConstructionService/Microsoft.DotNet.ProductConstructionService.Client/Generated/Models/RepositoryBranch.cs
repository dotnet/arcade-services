// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using Newtonsoft.Json;

namespace Microsoft.DotNet.ProductConstructionService.Client.Models
{
    public partial class RepositoryBranch
    {
        public RepositoryBranch(bool mergePrs)
        {
            MergePrs = mergePrs;
        }

        [JsonProperty("repository")]
        public string Repository { get; set; }

        [JsonProperty("branch")]
        public string Branch { get; set; }

        [JsonProperty("mergePrs")]
        public bool MergePrs { get; set; }

        [JsonProperty("ignoredChecks")]
        public List<string> IgnoredChecks { get; set; }
    }
}
