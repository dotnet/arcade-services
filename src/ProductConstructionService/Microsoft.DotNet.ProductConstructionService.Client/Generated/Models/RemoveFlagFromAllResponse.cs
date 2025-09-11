// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Newtonsoft.Json;

namespace Microsoft.DotNet.ProductConstructionService.Client.Models
{
    public partial class RemoveFlagFromAllResponse
    {
        public RemoveFlagFromAllResponse(bool success, int removedCount)
        {
            Success = success;
            RemovedCount = removedCount;
        }

        [JsonProperty("success")]
        public bool Success { get; set; }

        [JsonProperty("removedCount")]
        public int RemovedCount { get; set; }

        [JsonProperty("message")]
        public string Message { get; set; }
    }
}
