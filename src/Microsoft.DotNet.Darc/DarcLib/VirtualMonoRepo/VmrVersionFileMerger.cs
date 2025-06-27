// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.Models.VirtualMonoRepo;

#nullable enable
namespace Microsoft.DotNet.DarcLib.VirtualMonoRepo;

public interface IVmrVersionFileMerger
{
    /// <summary>
    /// Merges the changes in a JSON file between two references in the target repo and the VMR.
    /// </summary>
    Task MergeJsonAsync(
        Codeflow lastFlow,
        ILocalGitRepo targetRepo,
        string targetRepoPreviousRef,
        string targetRepoCurrentRef,
        ILocalGitRepo vmr,
        string vmrPreviousRef,
        string vmrCurrentRef,
        string mapping,
        string jsonRelativePath,
        CancellationToken cancellationToken);
}

public class VmrVersionFileMerger : IVmrVersionFileMerger
{
    private readonly IGitRepoFactory _gitRepoFactory;

    public VmrVersionFileMerger(IGitRepoFactory gitRepoFactory)
    {
        _gitRepoFactory = gitRepoFactory; ;
    }

    public async Task MergeJsonAsync(
        Codeflow lastFlow,
        ILocalGitRepo targetRepo,
        string targetRepoPreviousRef,
        string targetRepoCurrentRef,
        ILocalGitRepo vmr,
        string vmrPreviousRef,
        string vmrCurrentRef,
        string mapping,
        string jsonRelativePath,
        CancellationToken cancellationToken)
    {
        var targetRepoPreviousGlobalJson = await targetRepo.GetFileFromGitAsync(jsonRelativePath, targetRepoPreviousRef)
            ?? throw new FileNotFoundException($"File not found at {targetRepo.Path / jsonRelativePath} for reference {targetRepoPreviousRef}");
        var targetRepoCurrentGlobalJson = await targetRepo.GetFileFromGitAsync(jsonRelativePath, targetRepoCurrentRef)
            ?? throw new FileNotFoundException($"File not found at {targetRepo.Path / jsonRelativePath} for reference {targetRepoCurrentRef}");

        var vmrPreviousGlobalJson = lastFlow is Backflow 
            ? await vmr.GetFileFromGitAsync(VmrInfo.GetRelativeRepoSourcesPath(mapping) / jsonRelativePath, vmrPreviousRef)
                ?? throw new FileNotFoundException($"File not found at {vmr.Path / (VmrInfo.GetRelativeRepoSourcesPath(mapping) / jsonRelativePath)} for reference {vmrPreviousRef}")
            : targetRepoPreviousGlobalJson;
        var vmrCurrentGlobalJson = await vmr.GetFileFromGitAsync(VmrInfo.GetRelativeRepoSourcesPath(mapping) / jsonRelativePath, vmrCurrentRef)
            ?? throw new FileNotFoundException($"File not found at {vmr.Path / (VmrInfo.GetRelativeRepoSourcesPath(mapping) / jsonRelativePath)} for reference {vmrCurrentRef}");

        var targetRepoChanges = FlatJsonComparer.CompareFlatJsons(
            JsonFlattener.FlattenJsonToDictionary(targetRepoPreviousGlobalJson),
            JsonFlattener.FlattenJsonToDictionary(targetRepoCurrentGlobalJson));
        var vmrChanges = FlatJsonComparer.CompareFlatJsons(
            JsonFlattener.FlattenJsonToDictionary(vmrPreviousGlobalJson),
            JsonFlattener.FlattenJsonToDictionary(vmrCurrentGlobalJson));
        var globalJsonChanges = FlatJsonChangeComparer.ComputeChanges(targetRepoChanges, vmrChanges);

        var mergedGlobalJson = FlatJsonChangeComparer.ApplyChanges(
            targetRepoCurrentGlobalJson,
            globalJsonChanges);

        var newGlobalJson = new GitFile(jsonRelativePath, mergedGlobalJson);
        var repoClient = _gitRepoFactory.CreateClient(targetRepo.Path);
        // This doesn't actually commit the changes, it just adds them to the working tree
        await repoClient.CommitFilesAsync(
                [newGlobalJson],
                targetRepo.Path,
                targetRepoCurrentRef,
                $"Merge {jsonRelativePath} changes from VMR");
    }
}
