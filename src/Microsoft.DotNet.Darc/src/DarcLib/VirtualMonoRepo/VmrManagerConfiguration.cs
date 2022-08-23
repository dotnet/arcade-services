// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

namespace Microsoft.DotNet.DarcLib.VirtualMonoRepo;

public interface IVmrManagerConfiguration
{
    string TmpPath { get; set; }
    string VmrPath { get; set; }
}

public class VmrManagerConfiguration : IVmrManagerConfiguration
{
    public string VmrPath { get; set; }

    public string TmpPath { get; set; }

    public VmrManagerConfiguration(string vmrPath, string tmpPath)
    {
        VmrPath = vmrPath;
        TmpPath = tmpPath;
    }
}
