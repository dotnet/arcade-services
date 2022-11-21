// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

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

    public GitRepoClonerFactory(VmrRemoteConfiguration vmrRemoteConfig)
    {
        _vmrRemoteConfig = vmrRemoteConfig;
    }

    public IGitRepoCloner GetCloner(string repoUrl, ILogger logger)
    {
        Uri repoUri = new(repoUrl);

        if (repoUri.IsFile)
        {
            return new GitRepoCloner(string.Empty, logger);
        } 
        if (repoUri.Host == "github.com")
        {
            return new GitRepoCloner(_vmrRemoteConfig.GitHubToken, logger);
        }
        if (repoUri.Host == "dev.azure.com")
        {
            return new GitRepoCloner(_vmrRemoteConfig.AzureDevOpsToken, logger);
        }
        
        throw new NotImplementedException($"Unsupported repository remote {repoUrl}");
    }
}


