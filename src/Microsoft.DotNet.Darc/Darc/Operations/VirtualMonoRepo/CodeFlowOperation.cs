// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.DotNet.Darc.Helpers;
using Microsoft.DotNet.Darc.Options.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using Microsoft.Extensions.DependencyInjection;

#nullable enable
namespace Microsoft.DotNet.Darc.Operations.VirtualMonoRepo;

internal abstract class CodeFlowOperation : VmrOperationBase
{
    private readonly CodeFlowCommandLineOptions _options;

    protected CodeFlowOperation(CodeFlowCommandLineOptions options)
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

        if (_options.Build.HasValue && _options.Commit != null)
        {
            throw new ArgumentException("Cannot specify both --build and --ref");
        }

        string refToFlow = _options.Commit ?? DarcLib.Constants.HEAD;
        if (_options.Build.HasValue)
        {
            IBarApiClient barcLient = BarApiClientFactory.GetBarClient(_options, Logger);
            Maestro.Client.Models.Build build = await barcLient.GetBuildAsync(_options.Build.Value);
            refToFlow = build.Commit;
        }

        await FlowAsync(
            repoName,
            new NativePath(targetDirectory),
            refToFlow,
            _options.DiscardPatches,
            cancellationToken);
    }

    protected abstract Task FlowAsync(
        string mappingName,
        NativePath targetDirectory,
        string shaToFlow,
        bool discardPatches,
        CancellationToken cancellationToken);
}
