// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.DotNet.DarcLib.Helpers;

namespace Microsoft.DotNet.DarcLib.Models.VirtualMonoRepo;

public record CodeFlowResult(
        bool HadUpdates, 
        NativePath RepoPath,
        string LastFlowRepoSha,
        string LastFlowVmrSha);