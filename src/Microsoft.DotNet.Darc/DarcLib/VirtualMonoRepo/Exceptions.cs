﻿// Licensed to the .NET Foundation under one or more agreements.
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
    : DarcException
{
    public VmrIngestionPatch Patch { get; } = patch;
    public ProcessExecutionResult Result { get; } = result;

    public override string Message
        => $"Failed to {(reverseApply ? "reverse-apply" : "apply")} the patch {Path.GetFileName(Patch.Path)} to {Patch.ApplicationPath ?? "/"}."
            + Environment.NewLine
            + Environment.NewLine
            + Result;
}

public class PatchApplicationLeftConflictsException(IReadOnlyCollection<UnixPath> conflictedFiles, NativePath repoPath)
    : DarcException("The patch left the repository in a conflicted state")
{
    public IReadOnlyCollection<UnixPath> ConflictedFiles { get; } = conflictedFiles;
    public NativePath RepoPath { get; } = repoPath;
}

/// <summary>
///     Exception thrown when the service can't apply an update to the PR branch due to a conflict
///     between the source repo and a change that was made in the PR after it was opened.
/// </summary>
public class ConflictInPrBranchException : DarcException
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

public class NonLinearCodeflowException(string currentSha, string previousSha)
    : DarcException($"Cannot flow commit {currentSha} as it's not a descendant of previously flown commit {previousSha}")
{
}

/// <summary>
/// This exception is used when the current codeflow cannot be applied, and if a codeflow PR already exists, then it
/// is blocked from receiving new flows.
/// </summary>
public class BlockingCodeflowException(string msg) : DarcException(msg)
{
}
