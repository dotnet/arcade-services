// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Maestro.MergePolicyEvaluation;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using static Maestro.MergePolicies.CodeFlowMergePolicyInterpreter;

namespace Maestro.MergePolicies;
internal class CodeFlowMergePolicy : MergePolicy
{
    public override string DisplayName => "Code flow verification";

    public override async Task<MergePolicyEvaluationResult> EvaluateAsync(PullRequestUpdateSummary pr, IRemote darc)
    {
        CodeFlowMergePolicyInterpreter interpreter = pr.CodeFlowDirection switch
        {
            CodeFlowDirection.BackFlow => new BackFlowMergePolicyInterpreter(),
            CodeFlowDirection.ForwardFlow => new ForwardFlowMergePolicyInterpreter(),
            _ => throw new ArgumentOutOfRangeException("PR is not a codeflow PR, can't evaluate CodeFlow Merge Policy"),
        };

        CodeFlowMergePolicyInterpreterResult result = await interpreter.InterpretAsync(pr, darc);
        return result.IsSuccessful ?
            Succeed(result.Title) :
            Fail(result.Title, result.Message);
    }
}

public class CodeFlowMergePolicyBuilder : IMergePolicyBuilder
{
    public string Name => MergePolicyConstants.CodeflowMergePolicyName;

    public Task<IReadOnlyList<IMergePolicy>> BuildMergePoliciesAsync(MergePolicyProperties properties, PullRequestUpdateSummary pr)
    {
        return Task.FromResult<IReadOnlyList<IMergePolicy>>([new CodeFlowMergePolicy()]);
    }
}
