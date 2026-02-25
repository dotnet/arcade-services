// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Newtonsoft.Json.Linq;
using ProductConstructionService.WorkItems;

#nullable disable
namespace BuildInsights.BuildAnalysis.WorkItems.Models;

public class PullRequestGitHubEventWorkItem : WorkItem
{
    public string Action { get; set; }
    public bool Merged { get; set;  }
    public string Organization { get; set; }
    public string Repository { get; set; }
    public string HeadSha { get; set; }
    public long Number { get; set; }

    public static PullRequestGitHubEventWorkItem Parse(JObject data) => new()
    {
        Action = data.Value<string>("action"),
        Merged = data.Value<JObject>("pull_request").Value<bool>("merged"),
        Organization = data.Value<JObject>("organization")?.Value<string>("login")
            ?? data.Value<JObject>("repository").Value<JObject>("owner").Value<string>("login"),
        Repository = data.Value<JObject>("repository").Value<string>("name"),
        HeadSha = data.Value<JObject>("pull_request").Value<JObject>("head").Value<string>("sha"),
        Number = data.Value<int>("number"),
    };
}
