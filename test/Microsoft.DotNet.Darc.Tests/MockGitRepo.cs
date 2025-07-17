// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.Models;

namespace Microsoft.DotNet.Darc.Tests;

/// <summary>
/// Mock implementation of IGitRepo for testing purposes
/// </summary>
internal class MockGitRepo : IGitRepo
{
    private readonly Dictionary<string, string> _files = new();

    public void AddFile(string filePath, string content)
    {
        _files[filePath] = content;
    }

    public Task<string> GetFileContentsAsync(string filePath, string repoUri, string branch)
    {
        if (_files.TryGetValue(filePath, out var content))
        {
            return Task.FromResult(content);
        }
        throw new DependencyFileNotFoundException($"File {filePath} not found in mock repo");
    }

    public Task CommitFilesAsync(List<GitFile> filesToCommit, string repoUri, string branch, string message)
    {
        // For testing, we don't need to actually commit
        return Task.CompletedTask;
    }

    public Task<List<GitTreeItem>> LsTreeAsync(string uri, string gitRef, string path = null)
    {
        return Task.FromResult(new List<GitTreeItem>());
    }
}