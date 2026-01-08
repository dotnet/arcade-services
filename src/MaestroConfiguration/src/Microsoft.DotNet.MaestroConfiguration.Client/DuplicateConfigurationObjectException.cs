// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;

namespace Microsoft.DotNet.MaestroConfiguration.Client;

/// <summary>
/// Exception thrown when attempting to add a configuration object that already exists.
/// </summary>
public class DuplicateConfigurationObjectException : Exception
{
    public string FilePath { get; }
    public string Repository { get; set; }
    public string Branch { get; set; }

    public DuplicateConfigurationObjectException(string filePath, string repository, string branch)
        : base($"Configuration object with equivalent parameters already exists in '{filePath}' of repo {repository} on branch {branch}.")
    {
        FilePath = filePath;
        Repository = repository;
        Branch = branch;
    }
}
