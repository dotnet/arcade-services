// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.Models.VirtualMonoRepo;

#nullable enable
namespace Microsoft.DotNet.DarcLib.VirtualMonoRepo;

public interface IVmrInitializer
{
    /// <summary>
    /// Initializes new repo that hasn't been synchronized into the VMR yet.
    /// </summary>
    /// <param name="mappingName">Name of a repository mapping</param>
    /// <param name="targetRevision">Revision (commit SHA, branch, tag..) onto which to synchronize, leave empty for HEAD</param>
    /// <param name="initializeDependencies">When true, initializes dependencies (from Version.Details.xml) recursively</param>
    /// <param name="sourceMappingsPath">Path to the source-mappings.json file</param>
    /// <param name="codeFlowParameters">Record containing parameters for VMR initialization</param>
    /// <param name="lookUpBuilds">Whether to look up package versions and build number from BAR when populating version files</param>
    Task InitializeRepository(
        string mappingName,
        string? targetRevision,
        bool initializeDependencies,
        LocalPath sourceMappingsPath,
        CodeFlowParameters codeFlowParameters,
        bool lookUpBuilds,
        CancellationToken cancellationToken);
}
