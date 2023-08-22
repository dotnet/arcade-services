// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#nullable enable
using Microsoft.DotNet.DarcLib.Helpers;
using System;

namespace Microsoft.DotNet.DarcLib.VirtualMonoRepo;

public class VmrRemoteConfiguration
{
    public VmrRemoteConfiguration(string? gitHubToken, string? azureDevOpsToken)
    {
        GitHubToken = gitHubToken;
        AzureDevOpsToken = azureDevOpsToken;
    }

    public string? GitHubToken { get; }

    public string? AzureDevOpsToken { get; }

    public string? GetTokenForUri(string repoUri)
    {
        var repoType = GitRepoTypeParser.ParseFromUri(repoUri);

        return repoType switch
        {
            GitRepoType.GitHub => GitHubToken,
            GitRepoType.AzureDevOps => AzureDevOpsToken,
            GitRepoType.Local => null,
            _ => throw new NotImplementedException($"Unsupported repository remote {repoUri}"),
        };
    }
}
