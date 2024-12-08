// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using Newtonsoft.Json;

namespace Microsoft.DotNet.ProductConstructionService.Client.Models
{
    public partial class TrackedPullRequest
    {
        public TrackedPullRequest()
        {
        }

        [JsonProperty("url")]
        public string Url { get; set; }

        [JsonProperty("channel")]
        public string Channel { get; set; }

        [JsonProperty("targetBranch")]
        public string TargetBranch { get; set; }

        [JsonProperty("updates")]
        public List<PullRequestUpdate> Updates { get; set; }
    }
}
