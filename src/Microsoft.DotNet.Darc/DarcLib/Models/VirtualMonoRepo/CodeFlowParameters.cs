﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#nullable enable
namespace Microsoft.DotNet.DarcLib.Models.VirtualMonoRepo;

public record CodeFlowParameters(
    string? TpnTemplatePath,
    bool GenerateCodeOwners,
    bool GenerateCredScanSuppressions,
    bool DiscardPatches);
