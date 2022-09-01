// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.DotNet.Darc.Models.VirtualMonoRepo;

#nullable enable
namespace Microsoft.DotNet.DarcLib.VirtualMonoRepo;

public interface IVmrDependencyInfo
{
    string VmrPath { get; }

    string SourcesPath { get; }
    
    IReadOnlyCollection<SourceMapping> Mappings { get; }

    string GetRepoSourcesPath(SourceMapping mapping) => Path.Combine(SourcesPath, mapping.Name);

    Task UpdateDependencyVersion(SourceMapping mapping, string sha, string? version);

    Task<(string Sha, string? Version)?> GetDependencyVersion(SourceMapping mapping);
}

/// <summary>
/// Holds information about versions of individual repositories synchronized in the VMR.
/// Uses the AllRepoVersions.props file as source of truth and propagates changes into the git-info files.
/// </summary>
public class VmrDependencyInfo : IVmrDependencyInfo
{
    public const string SourceMappingsFileName = "source-mappings.json";
    public const string VmrSourcesPath = "src";
    public const string GitInfoSourcesPath = "git-info";


}
