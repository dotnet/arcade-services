// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.Models.VirtualMonoRepo;
using Microsoft.Extensions.Logging;

#nullable enable
namespace Microsoft.DotNet.DarcLib.VirtualMonoRepo;

public abstract class VmrManagerBase
{
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
    private readonly IFileSystem _fileSystem;
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
        IFileSystem fileSystem,
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
        _fileSystem = fileSystem;
    }

    protected async Task<IReadOnlyCollection<VmrIngestionPatch>> UpdateRepoToRevisionAsync(
        VmrDependencyUpdate update,
        ILocalGitRepo repoClone,
        string fromRevision,
        string commitMessage,
        bool restoreVmrPatches,
        CodeFlowParameters codeFlowParameters,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyCollection<VmrIngestionPatch> patches = await _patchHandler.CreatePatches(
            update.Mapping,
            repoClone,
            fromRevision,
            update.TargetRevision,
            _vmrInfo.TmpPath,
            _vmrInfo.TmpPath,
            codeFlowParameters.ApplyAdditionalMappings,
            cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        // Get a list of patches that need to be reverted for this update so that repo changes can be applied
        // This includes all patches that are also modified by the current change
        // (happens when we update repo from which the VMR patches come)
        IReadOnlyCollection<VmrIngestionPatch> vmrPatchesToRestore = restoreVmrPatches
            ? await StripVmrPatchesAsync(patches, codeFlowParameters.AdditionalRemotes, cancellationToken)
            : [];

        foreach (var patch in patches)
        {
            await _patchHandler.ApplyPatch(patch, _vmrInfo.VmrPath, codeFlowParameters.DiscardPatches, reverseApply: false, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
        }

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

        // Commit without adding files as they were added to index directly
        await CommitAsync(commitMessage);

        // TODO: Workaround for cases when we get CRLF problems on Windows
        // We should figure out why restoring and reapplying VMR patches leaves working tree with EOL changes
        // https://github.com/dotnet/arcade-services/issues/3277
        if (restoreVmrPatches && vmrPatchesToRestore.Any() && RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await _localGitClient.CheckoutAsync(_vmrInfo.VmrPath, ".");
        }

        return vmrPatchesToRestore;
    }

    protected async Task ReapplyVmrPatchesAsync(
        IReadOnlyCollection<VmrIngestionPatch> patches,
        CancellationToken cancellationToken)
    {
        if (patches.Count == 0)
        {
            return;
        }

        _logger.LogInformation("Re-applying {count} VMR patch{s}...",
            patches.Count,
            patches.Count > 1 ? "es" : string.Empty);

        foreach (var patch in patches.DistinctBy(p => p.Path).OrderBy(p => p.Path))
        {
            if (!_fileSystem.FileExists(patch.Path))
            {
                // Patch was removed, so it doesn't exist anymore
                _logger.LogDebug("Not re-applying {patch} as it was removed", patch.Path);
                continue;
            }

            await _patchHandler.ApplyPatch(patch, _vmrInfo.VmrPath, false, reverseApply: false, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
        }

        await CommitAsync("[VMR patches] Re-apply VMR patches");
        _logger.LogInformation("VMR patches re-applied back onto the VMR");
    }

    protected async Task CommitAsync(string commitMessage, (string Name, string Email)? author = null)
    {
        _logger.LogInformation("Committing..");

        var watch = Stopwatch.StartNew();

        await _localGitClient.CommitAsync(_vmrInfo.VmrPath, commitMessage, allowEmpty: true, author);

        _logger.LogInformation("Committed in {duration} seconds", (int)watch.Elapsed.TotalSeconds);
    }

    private async Task UpdateThirdPartyNoticesAsync(string templatePath, CancellationToken cancellationToken)
    {
        var isTpnUpdated = (await _localGitClient
            .GetStagedFiles(_vmrInfo.VmrPath))
            .Where(ThirdPartyNoticesGenerator.IsTpnPath)
            .Any();

        if (isTpnUpdated)
        {
            await _thirdPartyNoticesGenerator.UpdateThirdPartyNotices(templatePath);
            await _localGitClient.StageAsync(_vmrInfo.VmrPath, new[] { VmrInfo.ThirdPartyNoticesFileName }, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
        }
    }

    protected abstract Task<IReadOnlyCollection<VmrIngestionPatch>> StripVmrPatchesAsync(
        IReadOnlyCollection<VmrIngestionPatch> patches,
        IReadOnlyCollection<AdditionalRemote> additionalRemotes,
        CancellationToken cancellationToken);

    /// <summary>
    /// Takes a given commit message template and populates it with given values, URLs and others.
    /// </summary>
    /// <param name="template">Template into which the values are filled into</param>
    /// <param name="oldSha">SHA we are updating from</param>
    /// <param name="newSha">SHA we are updating to</param>
    /// <param name="additionalMessage">Additional message inserted in the commit body</param>
    protected static string PrepareCommitMessage(
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
            { "commitMessage", additionalMessage ?? string.Empty },
        };

        foreach (var replace in replaces)
        {
            template = template.Replace($"{{{replace.Key}}}", replace.Value);
        }

        return template;
    }
}
