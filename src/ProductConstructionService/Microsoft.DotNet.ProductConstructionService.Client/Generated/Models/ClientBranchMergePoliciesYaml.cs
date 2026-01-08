// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Immutable;
using Newtonsoft.Json;

namespace Microsoft.DotNet.ProductConstructionService.Client.Models
{
    public partial class ClientBranchMergePoliciesYaml
    {
        public ClientBranchMergePoliciesYaml(string branch, string repository)
        {
            Branch = branch;
            Repository = repository;
        }

        [JsonProperty("branch")]
        public string Branch { get; set; }

        [JsonProperty("repository")]
        public string Repository { get; set; }

        [JsonProperty("mergePolicies")]
        public IImmutableList<ClientMergePolicyYaml> MergePolicies { get; set; }

        [JsonIgnore]
        public bool IsValid
        {
            get
            {
                if (string.IsNullOrEmpty(Branch))
                {
                    return false;
                }
                if (string.IsNullOrEmpty(Repository))
                {
                    return false;
                }
                return true;
            }
        }
    }
}
