// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;

namespace Microsoft.DotNet.MaestroConfiguration.Client;

public class FileNotFoundInRepoException : Exception
{
    public FileNotFoundInRepoException(string repositoryUri, string branchName, string filePath)
        : base($"The file '{filePath}' was not found in repository '{repositoryUri}' on branch '{branchName}'.")
    {
    }
}
