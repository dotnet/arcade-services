﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.DotNet.Darc.Options.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;

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

        await vmrBackflower.BackflowAsync(repoName, targetDirectory, additionalRemotes);
    }
}
