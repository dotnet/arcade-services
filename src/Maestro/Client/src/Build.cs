// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Newtonsoft.Json;

namespace Microsoft.DotNet.Maestro.Client.Models
{
    public partial class Build
    {
        [JsonIgnore]
        public string Repository => GitHubRepository ?? AzureDevOpsRepository;

        [JsonIgnore]
        public string Branch => GitHubBranch ?? AzureDevOpsBranch;
    }
}
