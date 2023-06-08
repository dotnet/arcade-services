// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;

namespace Microsoft.DotNet.DarcLib.Helpers;

public enum GitRepoType
{
    GitHub,
    AzureDevOps,
    Local,
    None
}

public static class GitRepoTypeParser
{
    public static GitRepoType ParseFromUri(string pathOrUri)
    {
        if (!Uri.TryCreate(pathOrUri, UriKind.Absolute, out Uri parsedUri))
        {
            return GitRepoType.None;
        }

        return parsedUri switch
        {
            { IsFile: true } => GitRepoType.Local,
            { Host: "github.com" } => GitRepoType.GitHub,
            { Host: var host } when host is "dev.azure.com" => GitRepoType.AzureDevOps,
            { Host: var host } when host.EndsWith("visualstudio.com") => GitRepoType.AzureDevOps,
            _ => GitRepoType.None,
        };
    }
}
