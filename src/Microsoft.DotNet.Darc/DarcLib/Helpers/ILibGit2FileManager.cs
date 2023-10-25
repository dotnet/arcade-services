// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Threading.Tasks;

namespace Microsoft.DotNet.DarcLib;

public interface ILibGit2FileManager : IGitFileManager
{
    /// <summary>
    /// Add a new dependency to the repository
    /// </summary>
    /// <param name="dependency">Dependency to add.</param>
    /// <param name="repoUri">Repository URI to add the dependency to.</param>
    /// <param name="branch">Branch to add the dependency to.</param>
    /// <returns>Async task.</returns>
    Task AddDependencyAsync(
        DependencyDetail dependency,
        string repoUri,
        string branch);
}
