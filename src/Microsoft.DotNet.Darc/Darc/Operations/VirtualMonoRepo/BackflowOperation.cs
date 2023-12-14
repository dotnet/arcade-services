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

internal class BackflowOperation : VmrOperationBase<IVmrBackflower>
{
    private readonly BackflowCommandLineOptions _options;

    public BackflowOperation(BackflowCommandLineOptions options)
        : base(options)
    {
        _options = options;
    }

    protected override async Task ExecuteInternalAsync(
        IVmrBackflower vmrBackflower,
        string repoName,
        string? targetDirectory,
        IReadOnlyCollection<AdditionalRemote> additionalRemotes,
        CancellationToken cancellationToken)
    {
        targetDirectory ??= Path.Combine(
            _options.RepositoryDirectory ?? throw new ArgumentException($"No target directory specified for repository {repoName}"),
            repoName);

        if (!Directory.Exists(targetDirectory))
        {
            throw new FileNotFoundException($"Could not find directory {targetDirectory}");
        }

        if (_options.RepositoryDirectory is not null)
        {
            var vmrInfo = Provider.GetRequiredService<IVmrInfo>();
            vmrInfo.TmpPath = new NativePath(_options.RepositoryDirectory);
        }

        await vmrBackflower.FlowBackAsync(
            repoName,
            new NativePath(targetDirectory),
            shaToFlow: null, // TODO: Instead of flowing HEAD, we should support any SHA from commandline
            _options.DiscardPatches,
            cancellationToken: cancellationToken);
    }
}
