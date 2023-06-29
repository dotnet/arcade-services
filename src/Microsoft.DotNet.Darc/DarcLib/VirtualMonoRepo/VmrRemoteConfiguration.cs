// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#nullable enable
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
}
