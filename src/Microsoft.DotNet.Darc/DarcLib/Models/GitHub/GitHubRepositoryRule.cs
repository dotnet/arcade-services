// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Newtonsoft.Json;

namespace Microsoft.DotNet.DarcLib.Models.GitHub;

public class GitHubRepositoryRule
{
    [JsonProperty("type")]
    public string Type { get; set; }

    [JsonProperty("parameters")]
    public GitHubPullRequestRuleParameters Parameters { get; set; }
}

public class GitHubPullRequestRuleParameters
{
    [JsonProperty("require_last_push_approval")]
    public bool RequireLastPushApproval { get; set; }
}
