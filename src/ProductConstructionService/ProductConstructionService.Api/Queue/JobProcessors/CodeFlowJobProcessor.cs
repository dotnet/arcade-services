// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using Microsoft.DotNet.Maestro.Client;
using Microsoft.DotNet.Maestro.Client.Models;
using ProductConstructionService.Api.Queue.Jobs;

namespace ProductConstructionService.Api.Queue.JobProcessors;

internal class CodeFlowJobProcessor(
        IVmrInfo vmrInfo,
        IBasicBarClient barClient,
        IMaestroApi maestroApi,
        IPcsVmrBackFlower vmrBackFlower,
        IPcsVmrForwardFlower vmrForwardFlower,
        ILocalLibGit2Client gitClient,
        ITelemetryRecorder telemetryRecorder,
        ILogger<CodeFlowJobProcessor> logger)
    : IJobProcessor
{
    private readonly IVmrInfo _vmrInfo = vmrInfo;
    private readonly IBasicBarClient _barClient = barClient;
    private readonly IMaestroApi _maestroApi = maestroApi;
    private readonly IPcsVmrBackFlower _vmrBackFlower = vmrBackFlower;
    private readonly IPcsVmrForwardFlower _vmrForwardFlower = vmrForwardFlower;
    private readonly ILocalLibGit2Client _gitClient = gitClient;
    private readonly ITelemetryRecorder _telemetryRecorder = telemetryRecorder;
    private readonly ILogger<CodeFlowJobProcessor> _logger = logger;

    public async Task ProcessJobAsync(Job job, CancellationToken cancellationToken)
    {
        var codeflowJob = (CodeFlowJob)job;

        Subscription subscription = await _barClient.GetSubscriptionAsync(codeflowJob.SubscriptionId)
            ?? throw new Exception($"Subscription {codeflowJob.SubscriptionId} not found");

        if (!subscription.SourceEnabled || (subscription.SourceDirectory ?? subscription.TargetDirectory) == null)
        {
            throw new Exception($"Subscription {codeflowJob.SubscriptionId} is not source enabled or source directory is not set");
        }

        Build build = await _barClient.GetBuildAsync(codeflowJob.BuildId)
            ?? throw new Exception($"Build {codeflowJob.BuildId} not found");

        var isForwardFlow = subscription.TargetDirectory != null;

        _logger.LogInformation(
            "{direction}-flowing build {buildId} for subscription {subscriptionId} targeting {repo} / {targetBranch} to new branch {newBranch}",
            isForwardFlow ? "Forward" : "Back",
            build.Id,
            subscription.Id,
            subscription.TargetRepository,
            subscription.TargetBranch,
            codeflowJob.PrBranch);

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
                    codeflowJob.PrBranch,
                    cancellationToken);
            }
            else
            {
                (hadUpdates, targetRepo) = await _vmrBackFlower.FlowBackAsync(
                    subscription.SourceDirectory!,
                    build,
                    subscription.TargetBranch,
                    codeflowJob.PrBranch,
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
            await _gitClient.Push(targetRepo, codeflowJob.PrBranch, subscription.TargetRepository);
            scope.SetSuccess();
        }

        // When no PR is created yet, we notify Maestro that the branch is ready
        if (codeflowJob.PrUrl == null)
        {
            _logger.LogInformation(
                "Notifying Maestro that subscription code changes for {subscriptionId} are ready in local branch {branch}",
                subscription.Id,
                subscription.TargetBranch);

            await _maestroApi.Subscriptions.TriggerSubscriptionAsync(codeflowJob.BuildId, subscription.Id, default);
        }
    }
}
