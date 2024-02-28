// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using Microsoft.DotNet.Maestro.Client.Models;
using ProductConstructionService.Api.Queue.Jobs;

namespace ProductConstructionService.Api.Queue.JobProcessors;

public class CodeFlowJobProcessor(
        IVmrInfo vmrInfo,
        IBasicBarClient barClient,
        IVmrBackFlower vmrBackFlower,
        IVmrForwardFlower vmrForwardFlower,
        ILogger<CodeFlowJobProcessor> logger)
    : IJobProcessor
{
    private readonly IVmrInfo _vmrInfo = vmrInfo;
    private readonly IBasicBarClient _barClient = barClient;
    private readonly IVmrBackFlower _vmrBackFlower = vmrBackFlower;
    private readonly IVmrForwardFlower _vmrForwardFlower = vmrForwardFlower;
    private readonly ILogger<CodeFlowJobProcessor> _logger = logger;

    public async Task ProcessJobAsync(Job job, CancellationToken cancellationToken)
    {
        var codeflowJob = (CodeFlowJob)job;

        Subscription subscription = await _barClient.GetSubscriptionAsync(codeflowJob.SubscriptionId)
            ?? throw new Exception($"Subscription {codeflowJob.SubscriptionId} not found");

        if (!subscription.SourceEnabled || subscription.SourceDirectory == null)
        {
            throw new Exception($"Subscription {codeflowJob.SubscriptionId} is not source enabled or source directory is not set");
        }

        Build build = await _barClient.GetBuildAsync(codeflowJob.BuildId)
            ?? throw new Exception($"Build {codeflowJob.BuildId} not found");

        var isForwardFlow = build.GitHubRepository != _vmrInfo.VmrUri && build.AzureDevOpsRepository != _vmrInfo.VmrUri;
        var branchName = $"darc-{subscription.TargetBranch}-{Guid.NewGuid()}";

        _logger.LogInformation("{direction}-flowing build {buildId} for subscription {subscriptionId} targeting {repo} / {targetBranch} to new branch {newBranch}",
            isForwardFlow ? "Forward" : "Back",
            build.Id,
            subscription.Id,
            subscription.TargetRepository,
            subscription.TargetBranch,
            branchName);

        bool hadUpdates;

        try
        {
            if (isForwardFlow)
            {
                hadUpdates = await _vmrForwardFlower.FlowForwardAsync(
                    subscription.SourceDirectory,
                    build,
                    branchName,
                    subscription.TargetBranch,
                    cancellationToken);
            }
            else
            {
                hadUpdates = await _vmrBackFlower.FlowBackAsync(
                    subscription.SourceDirectory,
                    build,
                    branchName,
                    subscription.TargetBranch,
                    cancellationToken);
            }
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to flow changes for build {buildId} in subscription {subscriptionId}",
                build.Id,
                subscription.Id);
            return;
        }

        if (!hadUpdates)
        {
            _logger.LogInformation("There were no code-flow updates for subscription {subscriptionId}",
                subscription.Id);
        }
        else
        {
            _logger.LogInformation("Code changes for {subscriptionId} ready in branch {branch} {targetRepository}",
                subscription.Id,
                subscription.TargetBranch,
                subscription.TargetRepository);
        }
    }
}
