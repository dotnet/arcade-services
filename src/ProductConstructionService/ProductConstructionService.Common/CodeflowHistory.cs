// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.DotNet.DarcLib;

namespace ProductConstructionService.Common;

public record CodeflowHistory(
    List<Commit> Commits,
    List<CodeflowRecord> Codeflows);


public record CodeflowRecord(
    string SourceCommitSha,
    string TargetCommitSha,
    DateTimeOffset CodeflowMergeDate);
