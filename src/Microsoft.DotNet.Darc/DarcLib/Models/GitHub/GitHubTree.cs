// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using Newtonsoft.Json;

namespace Microsoft.DotNet.DarcLib.Models.GitHub;

public class GitHubTree
{
    [JsonProperty("base_tree")]
    public string BaseTree { get; set; }

    public List<GitHubTreeItem> Tree { get; set; }
}
