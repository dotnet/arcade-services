// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Newtonsoft.Json;

namespace Microsoft.DotNet.ProductConstructionService.Client.Models
{
    public partial class CodeflowStatus
    {
        public CodeflowStatus()
        {
        }

        [JsonProperty("mappingName")]
        public string MappingName { get; set; }

        [JsonProperty("repositoryUrl")]
        public string RepositoryUrl { get; set; }

        [JsonProperty("repositoryBranch")]
        public string RepositoryBranch { get; set; }

        [JsonProperty("forwardFlow")]
        public CodeflowSubscriptionStatus ForwardFlow { get; set; }

        [JsonProperty("backflow")]
        public CodeflowSubscriptionStatus Backflow { get; set; }
    }
}
