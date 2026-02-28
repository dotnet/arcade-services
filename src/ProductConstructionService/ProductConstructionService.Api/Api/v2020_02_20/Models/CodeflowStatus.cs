// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Newtonsoft.Json;

#nullable disable
namespace ProductConstructionService.Api.v2020_02_20.Models;

public class CodeflowStatus
{
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

public class CodeflowSubscriptionStatus
{
    [JsonProperty("subscription")]
    public Subscription Subscription { get; set; }

    [JsonProperty("inProgressPullRequestUrl")]
    public string InProgressPullRequestUrl { get; set; }

    [JsonProperty("staleness")]
    public int Staleness { get; set; }
}
