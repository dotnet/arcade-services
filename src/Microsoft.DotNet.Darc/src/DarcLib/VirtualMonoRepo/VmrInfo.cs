// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

#nullable enable
using System.IO;
using Microsoft.DotNet.Darc.Models.VirtualMonoRepo;

namespace Microsoft.DotNet.DarcLib.VirtualMonoRepo;

public interface IVmrInfo
{
    string TmpPath { get; }
    string VmrPath { get; }
    string? PatchesPath { get; set; }
    string GetRepoSourcesPath(SourceMapping mapping) => Path.Combine(VmrPath, VmrInfo.SourcesDir, mapping.Name);
}

public class VmrInfo : IVmrInfo
{
    public const string SourceMappingsFileName = "source-mappings.json";
    public const string SourcesDir = "src";
    public const string GitInfoSourcesDir = "git-info";

    public string VmrPath { get; }

    public string TmpPath { get; }

    public string? PatchesPath { get; set; }

    public VmrInfo(string vmrPath, string tmpPath)
    {
        VmrPath = vmrPath;
        TmpPath = tmpPath;
    }
}
