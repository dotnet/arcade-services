﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using Microsoft.DotNet.Maestro.Client;
using Microsoft.DotNet.Maestro.Client.Models;
using Microsoft.Extensions.Logging;
using ProductConstructionService.WorkItems.WorkItemDefinitions;

namespace ProductConstructionService.WorkItems.WorkItemProcessors;

internal class CodeFlowWorkItemProcessor(
        IVmrInfo vmrInfo,
        IBasicBarClient barClient,
        IMaestroApi maestroApi,
        IPcsVmrBackFlower vmrBackFlower,
        IPcsVmrForwardFlower vmrForwardFlower,
        ILocalLibGit2Client gitClient,
        ITelemetryRecorder telemetryRecorder,
        ILogger<CodeFlowWorkItemProcessor> logger)
    : IWorkItemProcessor
{
    private readonly IVmrInfo _vmrInfo = vmrInfo;
    private readonly IBasicBarClient _barClient = barClient;
    private readonly IMaestroApi _maestroApi = maestroApi;
    private readonly IPcsVmrBackFlower _vmrBackFlower = vmrBackFlower;
    private readonly IPcsVmrForwardFlower _vmrForwardFlower = vmrForwardFlower;
    private readonly ILocalLibGit2Client _gitClient = gitClient;
    private readonly ITelemetryRecorder _telemetryRecorder = telemetryRecorder;
    private readonly ILogger<CodeFlowWorkItemProcessor> _logger = logger;

    public async Task ProcessWorkItemAsync(WorkItem workItem, CancellationToken cancellationToken)
    {
        var codeflowWorkItem = (CodeFlowWorkItem)workItem;

        Subscription subscription = await _barClient.GetSubscriptionAsync(codeflowWorkItem.SubscriptionId)
            ?? throw new Exception($"Subscription {codeflowWorkItem.SubscriptionId} not found");

        if (!subscription.SourceEnabled || (subscription.SourceDirectory ?? subscription.TargetDirectory) == null)
        {
            throw new Exception($"Subscription {codeflowWorkItem.SubscriptionId} is not source enabled or source directory is not set");
        }

        Build build = await _barClient.GetBuildAsync(codeflowWorkItem.BuildId)
            ?? throw new Exception($"Build {codeflowWorkItem.BuildId} not found");

        var isForwardFlow = subscription.TargetDirectory != null;

        _logger.LogInformation(
            "{direction}-flowing build {buildId} for subscription {subscriptionId} targeting {repo} / {targetBranch} to new branch {newBranch}",
            isForwardFlow ? "Forward" : "Back",
            build.Id,
            subscription.Id,
            subscription.TargetRepository,
            subscription.TargetBranch,
            codeflowWorkItem.PrBranch);

        bool hadUpdates;
        NativePath targetRepo;

        try
        {
            if (isForwardFlow)
            {
                targetRepo = _vmrInfo.VmrPath;
                hadUpdates = await _vmrForwardFlower.FlowForwardAsync(
                    subscription.TargetDirectory!,
                    build,
                    subscription.TargetBranch,
                    codeflowWorkItem.PrBranch,
                    cancellationToken);
            }
            else
            {
                (hadUpdates, targetRepo) = await _vmrBackFlower.FlowBackAsync(
                    subscription.SourceDirectory!,
                    build,
                    subscription.TargetBranch,
                    codeflowWorkItem.PrBranch,
                    cancellationToken);
            }
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to flow changes for build {buildId} in subscription {subscriptionId}",
                build.Id,
                subscription.Id);
            throw;
        }

        if (!hadUpdates)
        {
            _logger.LogInformation("There were no code-flow updates for subscription {subscriptionId}",
                subscription.Id);
            return;
        }

        _logger.LogInformation("Code changes for {subscriptionId} ready in local branch {branch}",
            subscription.Id,
            subscription.TargetBranch);

        // TODO https://github.com/dotnet/arcade-services/issues/3318: Handle failures (conflict, non-ff etc)
        using (var scope = _telemetryRecorder.RecordGitOperation(TrackedGitOperation.Push, subscription.TargetRepository))
        {
            await _gitClient.Push(targetRepo, codeflowWorkItem.PrBranch, subscription.TargetRepository);
            scope.SetSuccess();
        }

        // When no PR is created yet, we notify Maestro that the branch is ready
        if (codeflowWorkItem.PrUrl == null)
        {
            _logger.LogInformation(
                "Notifying Maestro that subscription code changes for {subscriptionId} are ready in local branch {branch}",
                subscription.Id,
                subscription.TargetBranch);

            await _maestroApi.Subscriptions.TriggerSubscriptionAsync(codeflowWorkItem.BuildId, subscription.Id, default);
        }
    }
}
