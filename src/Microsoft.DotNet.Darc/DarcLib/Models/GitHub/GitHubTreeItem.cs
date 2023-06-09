// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.DotNet.DarcLib;

public class GitHubTreeItem
{
    public string Mode { get; set; }

    public string Type { get; set; }

    public string Path { get; set; }

    public string Content { get; set; }
}
