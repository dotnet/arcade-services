// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using Newtonsoft.Json;

namespace Microsoft.DotNet.ProductConstructionService.Client.Models
{
    public partial class FeatureFlagListResponse
    {
        public FeatureFlagListResponse(int total)
        {
            Total = total;
        }

        [JsonProperty("flags")]
        public List<FeatureFlagValue> Flags { get; set; }

        [JsonProperty("total")]
        public int Total { get; set; }
    }
}
