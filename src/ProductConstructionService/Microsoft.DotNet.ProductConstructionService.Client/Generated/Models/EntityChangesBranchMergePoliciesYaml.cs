// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using Newtonsoft.Json;

namespace Microsoft.DotNet.ProductConstructionService.Client.Models
{
    public partial class EntityChangesBranchMergePoliciesYaml
    {
        public EntityChangesBranchMergePoliciesYaml()
        {
        }

        [JsonProperty("creations")]
        public List<ClientBranchMergePoliciesYaml> Creations { get; set; }

        [JsonProperty("updates")]
        public List<ClientBranchMergePoliciesYaml> Updates { get; set; }

        [JsonProperty("removals")]
        public List<ClientBranchMergePoliciesYaml> Removals { get; set; }
    }
}
