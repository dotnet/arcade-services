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

    Task AddDependencyToGlobalJson(
        string repo,
        string branch,
        string parentField,
        string dependencyName,
        string version);

    Task AddDependencyToVersionDetailsAsync(
        string repo,
        string branch,
        DependencyDetail dependency);

    /// <summary>
    ///     <!-- Package versions -->
    ///     <PropertyGroup>
    ///         <MicrosoftDotNetApiCompatPackageVersion>1.0.0-beta.18478.5</MicrosoftDotNetApiCompatPackageVersion>
    ///     </PropertyGroup>
    ///
    ///     See https://github.com/dotnet/arcade/blob/main/Documentation/DependencyDescriptionFormat.md for more
    ///     information.
    /// </summary>
    /// <param name="repo">Path to Versions.props file</param>
    /// <param name="dependency">Dependency information to add.</param>
    /// <returns>Async task.</returns>
    Task AddDependencyToVersionsPropsAsync(
        string repo,
        string branch,
        DependencyDetail dependency);
}
