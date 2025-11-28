// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.DotNet.DarcLib.Models;
public record GitTreeItem
{
    public string Sha { get; init; }

    public string Path { get; init; }

    public string Type {
        get;
        init => field = value.ToLower();
    }

    public bool IsBlob() => Type == "blob";

    public bool IsTree() => Type == "tree";

    public bool IsCommit() => Type == "commit";
}
