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

/// <summary>
///     Exception thrown when the service can't apply an update to the PR branch due to a conflict
///     between the source repo and a change that was made in the PR after it was opened.
/// </summary>
public class ConflictInPrBranchException : Exception
{
    private static readonly Regex AlreadyExistsRegex = new("patch failed: (.+): already exist in index");
    private static readonly Regex PatchFailedRegex = new("error: patch failed: (.*):");
    private static readonly Regex PatchDoesNotApplyRegex = new("error: (.+): patch does not apply");
    private static readonly Regex FileDoesNotExistRegex = new("error: (.+): does not exist in index");
    private static readonly Regex FailedMergeRegex = new("CONFLICT (content): Merge conflict in (.+)");

    private static readonly Regex[] ConflictRegex =
    [
        AlreadyExistsRegex,
        PatchFailedRegex,
        PatchDoesNotApplyRegex,
        FileDoesNotExistRegex,
        FailedMergeRegex,
    ];

    public List<string> ConflictedFiles { get; }

    public ConflictInPrBranchException(
            string failedMergeMessage,
            string targetBranch,
            string mappingName,
            bool isForwardFlow)
        : this(ParseResult(failedMergeMessage, mappingName, isForwardFlow), targetBranch)
    {
    }

    private ConflictInPrBranchException(List<string> conflictedFiles, string targetBranch)
        : base($"Failed to flow changes due to conflicts in the target branch ({targetBranch})")
    {
        ConflictedFiles = conflictedFiles;
    }

    private static List<string> ParseResult(string failureException, string mappingName, bool isForwardFlow)
    {
        List<string> filesInConflict = [];
        var errors = failureException.Split(Environment.NewLine);
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
            return [..filesInConflict.Select(file => file.Split('/', 3)[2]).Distinct()];
        }
        else
        {
            // If we're backflowing, the file paths are already normalized
            return [..filesInConflict.Distinct().Select(file => $"src/{mappingName}/{file}")];
        }
    }
}

public class ManualCommitsInFlowException : Exception
{
    public ManualCommitsInFlowException(List<string> overwrittenCommits)
        : base("Failed to flow changes as they would overwrite manual changes to the PR")
    {
        OverwrittenCommits = overwrittenCommits;
    }

    public List<string> OverwrittenCommits { get; }
}
