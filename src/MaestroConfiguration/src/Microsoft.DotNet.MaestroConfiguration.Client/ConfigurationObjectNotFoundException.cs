// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;

namespace Microsoft.DotNet.MaestroConfiguration.Client;

/// <summary>
/// Exception thrown when a configuration object cannot be found.
/// </summary>
public class ConfigurationObjectNotFoundException : Exception
{
    public string FilePath { get; }

    public string RepositoryUri { get; }

    public string BranchName { get; }

    public ConfigurationObjectNotFoundException(string filePath, string repositoryUri, string branchName)
        : base($"Configuration object not found in '{filePath}' (Repository: {repositoryUri}, Branch: {branchName}).")
    {
        FilePath = filePath;
        RepositoryUri = repositoryUri;
        BranchName = branchName;
    }
}
