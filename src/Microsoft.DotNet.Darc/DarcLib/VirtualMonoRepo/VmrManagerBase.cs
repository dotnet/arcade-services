// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.Models.VirtualMonoRepo;
using Microsoft.Extensions.Logging;

#nullable enable
namespace Microsoft.DotNet.DarcLib.VirtualMonoRepo;

public abstract partial class VmrManagerBase
{
    [GeneratedRegex(@"\{(\w+)\}")]
    private static partial Regex TemplatePlaceholderRegex();

    protected const string InterruptedSyncExceptionMessage =
        "A new branch was created for the sync and didn't get merged as the sync " +
        "was interrupted. A new sync should start from {original} branch.";

    private readonly IVmrInfo _vmrInfo;
    private readonly IVmrDependencyTracker _dependencyInfo;
    private readonly IVmrPatchHandler _patchHandler;
    private readonly IThirdPartyNoticesGenerator _thirdPartyNoticesGenerator;
    private readonly ICodeownersGenerator _codeownersGenerator;
    private readonly ICredScanSuppressionsGenerator _credScanSuppressionsGenerator;
    private readonly ILocalGitClient _localGitClient;
    private readonly ILocalGitRepoFactory _localGitRepoFactory;
    private readonly ILogger _logger;

    protected ILocalGitRepo GetLocalVmr() => _localGitRepoFactory.Create(_vmrInfo.VmrPath);

    protected VmrManagerBase(
        IVmrInfo vmrInfo,
        IVmrDependencyTracker dependencyInfo,
        IVmrPatchHandler vmrPatchHandler,
        IThirdPartyNoticesGenerator thirdPartyNoticesGenerator,
        ICodeownersGenerator codeownersGenerator,
        ICredScanSuppressionsGenerator credScanSuppressionsGenerator,
        ILocalGitClient localGitClient,
        ILocalGitRepoFactory localGitRepoFactory,
        ILogger<VmrUpdater> logger)
    {
        _logger = logger;
        _vmrInfo = vmrInfo;
        _dependencyInfo = dependencyInfo;
        _patchHandler = vmrPatchHandler;
        _thirdPartyNoticesGenerator = thirdPartyNoticesGenerator;
        _codeownersGenerator = codeownersGenerator;
        _credScanSuppressionsGenerator = credScanSuppressionsGenerator;
        _localGitClient = localGitClient;
        _localGitRepoFactory = localGitRepoFactory;
    }

    protected async Task<IReadOnlyCollection<UnixPath>> UpdateRepoToRevisionAsync(
        VmrDependencyUpdate update,
        ILocalGitRepo repoClone,
        string fromRevision,
        string commitMessage,
        bool restoreVmrPatches,
        bool keepConflicts,
        CodeFlowParameters codeFlowParameters,
        string[]? additionalFileExclusions = null,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyCollection<VmrIngestionPatch> patches = await _patchHandler.CreatePatches(
            update.Mapping,
            repoClone,
            fromRevision,
            update.TargetRevision,
            _vmrInfo.TmpPath,
            _vmrInfo.TmpPath,
            additionalFileExclusions,
            cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        IReadOnlyCollection<UnixPath> conflicts = await _patchHandler.ApplyPatches(
            patches,
            _vmrInfo.VmrPath,
            removePatchAfter: true,
            keepConflicts: keepConflicts,
            cancellationToken: cancellationToken);

        _dependencyInfo.UpdateDependencyVersion(update);

        var filesToAdd = new List<string>
        {
            _vmrInfo.SourceManifestPath
        };

        await _localGitClient.StageAsync(_vmrInfo.VmrPath, filesToAdd, cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();

        if (codeFlowParameters.TpnTemplatePath != null)
        {
            await UpdateThirdPartyNoticesAsync(codeFlowParameters.TpnTemplatePath, cancellationToken);
        }

        if (codeFlowParameters.GenerateCodeOwners)
        {
            await _codeownersGenerator.UpdateCodeowners(cancellationToken);
        }

        if (codeFlowParameters.GenerateCredScanSuppressions)
        {
            await _credScanSuppressionsGenerator.UpdateCredScanSuppressions(cancellationToken);
        }

        if (conflicts.Count == 0)
        {
            // Commit without adding files as they were added to index directly
            await CommitAsync(commitMessage);

            // TODO: Workaround for cases when we get CRLF problems on Windows
            // We should figure out why restoring and reapplying VMR patches leaves working tree with EOL changes
            // https://github.com/dotnet/arcade-services/issues/3277
            if (restoreVmrPatches && RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                cancellationToken.ThrowIfCancellationRequested();
                await _localGitClient.CheckoutAsync(_vmrInfo.VmrPath, ".");
            }
        }

        return conflicts;
    }

    protected virtual async Task CommitAsync(string commitMessage, (string Name, string Email)? author = null)
    {
        var watch = Stopwatch.StartNew();

        await _localGitClient.CommitAsync(_vmrInfo.VmrPath, commitMessage, allowEmpty: true, author);

        _logger.LogDebug("Committed in {duration} seconds", (int)watch.Elapsed.TotalSeconds);
    }

    private async Task UpdateThirdPartyNoticesAsync(string templatePath, CancellationToken cancellationToken)
    {
        var isTpnUpdated = (await _localGitClient
            .GetStagedFilesAsync(_vmrInfo.VmrPath))
            .Where(ThirdPartyNoticesGenerator.IsTpnPath)
            .Any();

        if (isTpnUpdated)
        {
            await _thirdPartyNoticesGenerator.UpdateThirdPartyNotices(templatePath);
            await _localGitClient.StageAsync(_vmrInfo.VmrPath, new[] { VmrInfo.ThirdPartyNoticesFileName }, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
        }
    }

    /// <summary>
    /// Takes a given commit message template and populates it with given values, URLs and others.
    /// </summary>
    /// <param name="template">Template into which the values are filled into</param>
    /// <param name="oldSha">SHA we are updating from</param>
    /// <param name="newSha">SHA we are updating to</param>
    /// <param name="additionalMessage">Additional message inserted in the commit body</param>
    public static string PrepareCommitMessage(
        string template,
        string name,
        string remote,
        string? oldSha = null,
        string? newSha = null,
        string? additionalMessage = null)
    {
        var replaces = new Dictionary<string, string?>
        {
            { "name", name },
            { "remote", remote },
            { "oldSha", oldSha },
            { "newSha", newSha },
            { "oldShaShort", oldSha is null ? string.Empty : Commit.GetShortSha(oldSha) },
            { "newShaShort", newSha is null ? string.Empty : Commit.GetShortSha(newSha) },
            { "additionalMessage", additionalMessage ?? string.Empty },
        };

        return TemplatePlaceholderRegex().Replace(template, match =>
        {
            var key = match.Groups[1].Value;
            return replaces.TryGetValue(key, out var value) ? value ?? string.Empty : match.Value;
        });
    }
}
