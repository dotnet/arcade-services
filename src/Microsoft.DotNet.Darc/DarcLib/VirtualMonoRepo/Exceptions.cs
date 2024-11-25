// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.IO;
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

public class ConflictInPrBranchException(VmrIngestionPatch patch, string targetBranch)
    : Exception($"Failed to flow changes due to conflicts in the target branch ({targetBranch})")
{
    public VmrIngestionPatch Patch { get; } = patch;
}
