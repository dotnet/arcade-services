// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Newtonsoft.Json;

#nullable disable
namespace BuildInsights.GitHubGraphQL.GitHubGraphQLAPI;

public class GitHubGraphQLFieldValue
{
    [JsonProperty("__typename")]
    public string TypeName { get; set; }
    public string Text { get; set; }
    public string Name { get; set; }
    public GitHubGraphQLField Field { get; set; }
}
