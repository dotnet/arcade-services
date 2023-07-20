// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.Extensions.Logging;
using System;

#nullable enable
namespace Microsoft.DotNet.DarcLib.VirtualMonoRepo;

public interface IGitRepoClonerFactory
{
    IGitRepoCloner GetCloner(string repoUrl, ILogger logger);
}

public class GitRepoClonerFactory : IGitRepoClonerFactory
{
    private readonly VmrRemoteConfiguration _vmrRemoteConfig;
    private readonly IProcessManager _processManager;

    public GitRepoClonerFactory(VmrRemoteConfiguration vmrRemoteConfig, IProcessManager processManager)
    {
        _vmrRemoteConfig = vmrRemoteConfig;
        _processManager = processManager;
    }

    public IGitRepoCloner GetCloner(string repoUri, ILogger logger) => GitRepoTypeParser.ParseFromUri(repoUri) switch
    {
        GitRepoType.GitHub => new GitNativeRepoCloner(_processManager, logger),
        GitRepoType.AzureDevOps => new GitNativeRepoCloner(_processManager, logger),
        GitRepoType.Local => new GitNativeRepoCloner(_processManager, logger),
        _ => throw new NotImplementedException($"Unsupported repository remote {repoUri}"),
    };
}
