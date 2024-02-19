// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.DotNet.DarcLib.Helpers;

namespace Microsoft.DotNet.DarcLib.VirtualMonoRepo;

public interface IVmrCloner
{
    Task PrepareVmrCloneAsync(CancellationToken cancellationToken);
}

public class VmrCloner(
    ILocalGitClient localGitClient,
    IProcessManager processManager,
    IVmrInfo vmrInfo,
    string vmrLocation) : IVmrCloner
{
    private readonly string _vmrLocation = vmrLocation;

    public async Task PrepareVmrCloneAsync(CancellationToken cancellationToken)
    {
        List<string> args = new();
        Dictionary<string, string> envVars = new();
        localGitClient.AddGitAuthHeader(args, envVars, Constants.DefaultVmrUri);

        args.Add("clone");
        args.Add(Constants.DefaultVmrUri);
        args.Add(_vmrLocation);

        ProcessExecutionResult result = await processManager.Execute("git", args, envVariables: envVars, cancellationToken: cancellationToken);
        result.ThrowIfFailed("Failed to clone the virtual mono-repo");

        vmrInfo.VmrPath = new NativePath(_vmrLocation);
    }
}
