// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.DotNet.Darc.Models.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib.Helpers;

#nullable enable
namespace Microsoft.DotNet.DarcLib.VirtualMonoRepo;

/// <summary>
/// Patch that is created/applied during sync of the VMR.
/// </summary>
/// <param name="Path">Path where the patch is located</param>
/// <param name="ApplicationPath">Relative path within the VMR to which the patch is applied onto</param>
public record VmrIngestionPatch
{
    public string Path { get; }

    public UnixPath? ApplicationPath { get; }

    public VmrIngestionPatch(string path, string? applicationPath)
    {
        Path = path;

        if (applicationPath != null)
        {
            ApplicationPath = new UnixPath(applicationPath);
        }
    }

    public VmrIngestionPatch(string path, SourceMapping targetMapping)
    {
        Path = path;
        ApplicationPath = VmrInfo.GetRelativeRepoSourcesPath(targetMapping);
    }
}
