// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.DotNet.Darc.Options.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

#nullable enable
namespace Microsoft.DotNet.Darc.Operations.VirtualMonoRepo;

internal abstract class VmrOperationBase<TVmrManager> : Operation where TVmrManager : notnull
{
    private readonly IBaseVmrCommandLineOptions _options;

    protected VmrOperationBase(IBaseVmrCommandLineOptions options)
        : base(options, options.RegisterServices())
    {
        _options = options;
    }

    /// <summary>
    /// Executes any VMR command by running it for every repository set in the arguments.
    /// </summary>
    public override async Task<int> ExecuteAsync()
    {
        var repositories = _options.Repositories.ToList();

        if (!repositories.Any())
        {
            Logger.LogError("Please specify at least one repository to synchronize");
            return Constants.ErrorCode;
        }

        TVmrManager vmrManager = Provider.GetRequiredService<TVmrManager>();

        IEnumerable<(string Name, string? Revision)> repoNamesWithRevisions = repositories
            .Select(a => a.Split(':', 2) is string[] parts && parts.Length == 2
                ? (Name: parts[0], Revision: parts[1])
                : (a, null));

        IReadOnlyCollection<AdditionalRemote> additionalRemotes = Array.Empty<AdditionalRemote>();
        if (_options.AdditionalRemotes != null)
        {
            additionalRemotes = _options.AdditionalRemotes
                .Select(a => a.Split(':', 2))
                .Select(parts => new AdditionalRemote(parts[0], parts[1]))
                .ToImmutableArray();
        }

        var success = true;

        // We have a graceful cancellation to not leave the git repo in some inconsistent state
        // This is mainly useful for manual use but can be also useful in CI when we time out but still want to push what we committed
        using var listener = CancellationKeyListener.ListenForCancellation(Logger);

        try
        {
            foreach (var (repoName, revision) in repoNamesWithRevisions)
            {
                listener.Token.ThrowIfCancellationRequested();
                success &= await ExecuteAsync(vmrManager, repoName, revision, additionalRemotes, listener.Token);
            }
        }
        catch (OperationCanceledException)
        {
            return Constants.ErrorCode;
        }

        if (listener.Token.IsCancellationRequested)
        {
            return Constants.ErrorCode;
        }

        return success ? Constants.SuccessCode : Constants.ErrorCode;
    }

    protected abstract Task ExecuteInternalAsync(
        TVmrManager vmrManager,
        string repoName,
        string? targetRevision,
        IReadOnlyCollection<AdditionalRemote> additionalRemotes,
        CancellationToken cancellationToken);

    private async Task<bool> ExecuteAsync(
        TVmrManager vmrManager,
        string repoName,
        string? targetRevision,
        IReadOnlyCollection<AdditionalRemote> additionalRemotes,
        CancellationToken cancellationToken)
    {
        using (Logger.BeginScope(repoName))
        {
            try
            {
                await ExecuteInternalAsync(vmrManager, repoName, targetRevision, additionalRemotes, cancellationToken);
                return true;
            }
            catch (EmptySyncException e)
            {
                Logger.LogInformation("{message}", e.Message);
                return true;
            }
            catch (Exception e)
            {
                Logger.LogError(
                    "Failed to synchronize repo {name}{exception}.", 
                    repoName, 
                    Environment.NewLine + e.Message);

                Logger.LogDebug("{exception}", e);
                return false;
            }
        }
    }
}
