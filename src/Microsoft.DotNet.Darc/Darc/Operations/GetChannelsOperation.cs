// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Threading.Tasks;
using Microsoft.DotNet.Darc.Options;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;

namespace Microsoft.DotNet.Darc.Operations;

internal class GetChannelsOperation : Operation
{
    private readonly GetChannelsCommandLineOptions _options;
    private readonly IVmrCloneManager _cloneManager;
    private readonly IVmrInfo _vmrInfo;
    private readonly CodeFlowConflictResolver _conflictResolver;

    public GetChannelsOperation(
        GetChannelsCommandLineOptions options,
        IVmrCloneManager cloneManager,
        IVmrInfo vmrInfo,
        CodeFlowConflictResolver conflictResolver)
    {
        _options = options;
        _cloneManager = cloneManager;
        _vmrInfo = vmrInfo;
        _conflictResolver = conflictResolver;
    }

    /// <summary>
    /// Retrieve information about channels
    /// </summary>
    /// <param name="options">Command line options</param>
    /// <returns>Process exit code.</returns>
    public override async Task<int> ExecuteAsync()
    {
        var path = @"C:\Users\prvysoky\AppData\Local\Temp\_vmrTests\xobuyxuz.hsi\_tests\vxmoqvty.lmh\vmr";
        var targetBranch = "main";
        var prBranch = "OutOfOrderMergesTest-ff";

        _vmrInfo.VmrPath = new NativePath(path);
        await _cloneManager.PrepareVmrAsync([path], [targetBranch, prBranch], prBranch, default);

        if (await _conflictResolver.TryMergingTargetBranch("product-repo1", prBranch, targetBranch))
        {
            Console.WriteLine("yay");
        }
        else
        {
            Console.WriteLine("nay");
        }

        return Constants.SuccessCode;
    }
}
