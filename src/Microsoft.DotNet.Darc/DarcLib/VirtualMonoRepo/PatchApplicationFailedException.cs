// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.IO;
using Microsoft.DotNet.DarcLib.Helpers;

#nullable enable
namespace Microsoft.DotNet.DarcLib.VirtualMonoRepo;

internal class PatchApplicationFailedException : Exception
{
    public PatchApplicationFailedException(VmrIngestionPatch patch, ProcessExecutionResult result, bool reverseApply)
        : base($"Failed to {(reverseApply ? "reverse-apply" : "apply")} the patch {Path.GetFileName(patch.Path)} to {patch.ApplicationPath ?? "/"}."
            + Environment.NewLine
            + Environment.NewLine
            + result)
    {
    }
}
