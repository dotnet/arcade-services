// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.DotNet.DarcLib.Helpers;

#nullable enable
namespace Microsoft.DotNet.DarcLib.VirtualMonoRepo;

public class PatchApplicationFailedException(
        VmrIngestionPatch patch,
        ProcessExecutionResult result,
        bool reverseApply)
    : Exception(GetExceptionMessage(patch, result, reverseApply))
{
    public VmrIngestionPatch Patch { get; } = patch;
    public ProcessExecutionResult Result { get; } = result;

    private static string GetExceptionMessage(VmrIngestionPatch patch, ProcessExecutionResult result, bool reverseApply)
        => $"Failed to {(reverseApply ? "reverse-apply" : "apply")} the patch {Path.GetFileName(patch.Path)} to {patch.ApplicationPath ?? "/"}."
            + Environment.NewLine
            + Environment.NewLine
            + result;
}

public class ConflictInPrBranchException(ProcessExecutionResult gitMergeResult, string targetBranch, bool isForwardFlow)
    : Exception($"Failed to flow changes due to conflicts in the target branch ({targetBranch})")
{
    public List<string> FilesInConflict { get; } = ParseResult(gitMergeResult, isForwardFlow);

    private static readonly Regex AlreadyExistsRegex = new("patch failed: (.+): already exist in index");
    private static readonly Regex PatchFailedRegex = new("error: patch failed: (.*):");
    private static readonly Regex PatchDoesNotApplyRegex = new("error: (.+): patch does not apply");
    private static readonly Regex FileDoesNotExistRegex = new("error: (.+): does not exist in index");

    private static readonly Regex[] ConflictRegex =
        [
            AlreadyExistsRegex,
            PatchFailedRegex,
            PatchDoesNotApplyRegex,
            FileDoesNotExistRegex
        ];

    private static List<string> ParseResult(ProcessExecutionResult gitMergeResult, bool isForwardFlow)
    {
        List<string> filesInConflict = new();
        var errors = gitMergeResult.StandardError.Split(Environment.NewLine);
        foreach (var error in errors)
        {
            foreach (var regex in ConflictRegex)
            {
                var match = regex.Match(error);
                if (match.Success)
                {
                    filesInConflict.Add(match.Groups[1].Value);
                    break;
                }
            }
        }
        if (isForwardFlow)
        {
            // Convert VMR paths to normal repo paths, for example src/repo/file.cs -> file.cs
            return filesInConflict.Select(file => file.Split('/', 3)[2]).Distinct().ToList();
        }
        // If we're backflowing, the file paths are already normal
        return filesInConflict;
    }
}
