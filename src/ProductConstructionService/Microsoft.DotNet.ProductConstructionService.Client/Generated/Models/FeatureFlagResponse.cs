// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Newtonsoft.Json;

namespace Microsoft.DotNet.ProductConstructionService.Client.Models
{
    public partial class FeatureFlagResponse
    {
        public FeatureFlagResponse(bool success)
        {
            Success = success;
        }

        [JsonProperty("success")]
        public bool Success { get; set; }

        [JsonProperty("message")]
        public string Message { get; set; }

        [JsonProperty("flag")]
        public FeatureFlagValue Flag { get; set; }
    }
}
