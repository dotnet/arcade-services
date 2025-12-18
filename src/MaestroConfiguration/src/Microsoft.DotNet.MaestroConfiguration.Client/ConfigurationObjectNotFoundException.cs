// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;

namespace Microsoft.DotNet.MaestroConfiguration.Client;

/// <summary>
/// Exception thrown when a configuration object cannot be found.
/// </summary>
public class ConfigurationObjectNotFoundException : Exception
{
    /// <summary>
    /// Gets the file path where the object was expected to be found.
    /// </summary>
    public string FilePath { get; }

    /// <summary>
    /// Gets the repository URI where the search was performed.
    /// </summary>
    public string RepositoryUri { get; }

    /// <summary>
    /// Gets the branch name where the search was performed.
    /// </summary>
    public string BranchName { get; }

    public ConfigurationObjectNotFoundException(string message, string filePath, string repositoryUri, string branchName)
        : base(message)
    {
        FilePath = filePath;
        RepositoryUri = repositoryUri;
        BranchName = branchName;
    }

    public ConfigurationObjectNotFoundException(string message, string filePath, string repositoryUri, string branchName, Exception innerException)
        : base(message, innerException)
    {
        FilePath = filePath;
        RepositoryUri = repositoryUri;
        BranchName = branchName;
    }
}
