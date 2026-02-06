// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Helpers;

namespace ProductConstructionService.ScenarioTests;
internal class NoRemoteFactory : IRemoteFactory
{
    public Task<IDependencyFileManager> CreateDependencyFileManagerAsync(string repoUrl) => throw new NotImplementedException();
    public Task<IRemote> CreateRemoteAsync(string repoUrl) => throw new NotImplementedException();
}
