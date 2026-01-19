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
