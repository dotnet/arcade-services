// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.DotNet.DarcLib.Helpers;
using System.Threading.Tasks;

namespace Microsoft.DotNet.DarcLib;

/// <summary>
///     For certain use cases where components will step between various remote repositories
///     (e.g. between azure devops and github).
/// </summary>
public interface IRemoteFactory
{
    Task<IRemote> CreateRemoteAsync(string repoUrl);

    Task<IDependencyFileManager> CreateDependencyFileManagerAsync(string repoUrl);
}
