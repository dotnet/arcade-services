// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.DotNet.Darc.Options.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;

#nullable enable
namespace Microsoft.DotNet.Darc.Operations.VirtualMonoRepo;

internal class BackflowOperation : VmrOperationBase<IVmrBackflower>
{
    private readonly BackflowCommandLineOptions _options;

    public static IImmutableDictionary<string, BackflowAction> Actions { get; } = new Dictionary<string, BackflowAction>
    {
        { "create-patches", BackflowAction.CreatePatches },
        { "apply-patches", BackflowAction.ApplyPatches },
    }.ToImmutableDictionary();

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

        await vmrBackflower.BackflowAsync(
            ParseAction(_options.Action),
            repoName,
            new NativePath(targetDirectory),
            additionalRemotes,
            cancellationToken);
    }

    private static BackflowAction ParseAction(string value)
    {
        if (!Actions.TryGetValue(value, out BackflowAction action))
        {
            throw new ArgumentException($"Invalid action {value}. Allowed values are: {string.Join(", ", Actions.Keys)}");
        }

        return action;
    }
}
