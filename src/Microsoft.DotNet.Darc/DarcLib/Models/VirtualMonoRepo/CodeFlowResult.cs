// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.Models.Darc;

namespace Microsoft.DotNet.DarcLib.Models.VirtualMonoRepo;

public record CodeFlowResult(
        bool hadUpdates, 
        NativePath repoPath,
        string previousFlowRepoSha,
        string previousFlowVmrSha,
        List<DependencyUpdate> dependencyUpdates);
