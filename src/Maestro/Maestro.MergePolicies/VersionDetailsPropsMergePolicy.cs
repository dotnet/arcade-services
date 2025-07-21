// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Threading.Tasks;
using Maestro.MergePolicyEvaluation;
using Microsoft.DotNet.DarcLib;

namespace Maestro.MergePolicies;
internal class VersionDetailsPropsMergePolicy : MergePolicy
{
    public override string Name => "VersionDetailsProps";

    public override string DisplayName => "Version Details Properties Merge Policy";

    public override Task<MergePolicyEvaluationResult> EvaluateAsync(PullRequestUpdateSummary pr, IRemote darc) => throw new NotImplementedException();
}
