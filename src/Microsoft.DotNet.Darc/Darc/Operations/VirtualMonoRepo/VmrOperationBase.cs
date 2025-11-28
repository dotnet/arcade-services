// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.DotNet.Darc.Helpers;
using Microsoft.DotNet.Darc.Options.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using Microsoft.Extensions.Logging;

#nullable enable
namespace Microsoft.DotNet.Darc.Operations.VirtualMonoRepo;

internal abstract class VmrOperationBase : Operation
{
    private readonly IBaseVmrCommandLineOptions _options;
    private readonly ILogger<VmrOperationBase> _logger;

    protected VmrOperationBase(
        IBaseVmrCommandLineOptions options,
        ILogger<VmrOperationBase> logger)
    {
        _options = options;
        _logger = logger;
    }

    /// <summary>
    /// Executes any VMR command by running it for every repository set in the arguments.
    /// </summary>
    public override async Task<int> ExecuteAsync()
    {
        var repositories = _options.Repositories.ToList();

        if (repositories.Count == 0)
        {
            _logger.LogError("Please specify at least one repository to synchronize");
            return Constants.ErrorCode;
        }

        // Repository names are in the form of NAME or NAME:REVISION where REVISION is a git ref
        // No REVISION means synchronizing the current HEAD
        IEnumerable<(string Name, string? Revision)> repoNamesWithRevisions = repositories
            .Select(a => a.Split(':', 2) is string[] parts && parts.Length == 2
                ? (Name: parts[0], Revision: parts[1])
                : (a, null));

        // Additional remotes are in the form of [mapping name]:[remote URI]
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
        using var listener = CancellationKeyListener.ListenForCancellation(_logger);

        try
        {
            foreach (var (repoName, revision) in repoNamesWithRevisions)
            {
                listener.Token.ThrowIfCancellationRequested();
                success &= await ExecuteAsync(repoName, revision, additionalRemotes, listener.Token);
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
        string repoName,
        string? targetRevision,
        IReadOnlyCollection<AdditionalRemote> additionalRemotes,
        CancellationToken cancellationToken);

    private async Task<bool> ExecuteAsync(
        string repoName,
        string? targetRevision,
        IReadOnlyCollection<AdditionalRemote> additionalRemotes,
        CancellationToken cancellationToken)
    {
        using (_logger.BeginScope(repoName))
        {
            try
            {
                await ExecuteInternalAsync(repoName, targetRevision, additionalRemotes, cancellationToken);
                return true;
            }
            catch (EmptySyncException e)
            {
                _logger.LogInformation("{message}", e.Message);
                return true;
            }
            catch (PatchApplicationLeftConflictsException e)
            {
                _logger.LogWarning(
                    "Conflicts occurred during the synchronization of {name}. Changes are staged and conflict left to be resolved in the working tree.",
                    repoName);
                _logger.LogDebug("{exception}", e);
                return false;
            }
            catch (Exception e)
            {
                _logger.LogError(
                    "The command terminated unsuccessfully. {exception}.", 
                    Environment.NewLine + e.Message);

                _logger.LogDebug("{exception}", e);
                return false;
            }
        }
    }
}
