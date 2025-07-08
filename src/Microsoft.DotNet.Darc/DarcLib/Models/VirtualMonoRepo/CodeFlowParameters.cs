// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#nullable enable
using System.Collections.Generic;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;

namespace Microsoft.DotNet.DarcLib.Models.VirtualMonoRepo;

public record CodeFlowParameters(
    IReadOnlyCollection<AdditionalRemote> AdditionalRemotes,
    string? TpnTemplatePath,
    bool GenerateCodeOwners,
    bool GenerateCredScanSuppressions);
