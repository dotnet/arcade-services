// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.DotNet.Darc.Options.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using Microsoft.Extensions.DependencyInjection;

#nullable enable
namespace Microsoft.DotNet.Darc.Operations.VirtualMonoRepo;

internal class ForwardFlowOperation : VmrOperationBase
{
    private readonly ForwardFlowCommandLineOptions _options;

    public ForwardFlowOperation(ForwardFlowCommandLineOptions options)
        : base(options)
    {
        _options = options;
    }

    protected override async Task ExecuteInternalAsync(
        string repoName,
        string? targetDirectory,
        IReadOnlyCollection<AdditionalRemote> additionalRemotes,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(targetDirectory);

        var targetDir = new NativePath(targetDirectory) / repoName;

        if (!Directory.Exists(targetDir))
        {
            throw new FileNotFoundException($"Could not find directory {targetDir}");
        }

        var forwardFlower = Provider.GetRequiredService<IVmrForwardFlower>();

        await forwardFlower.FlowForwardAsync(
            repoName,
            targetDir,
            shaToFlow: null, // TODO: Instead of flowing HEAD, we should support any SHA from commandline
            _options.DiscardPatches,
            cancellationToken: cancellationToken);
    }
}
