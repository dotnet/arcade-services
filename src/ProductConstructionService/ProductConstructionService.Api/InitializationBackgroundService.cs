// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using ProductConstructionService.Api.Queue;

namespace ProductConstructionService.Api;

internal class InitializationBackgroundService(
        IVmrCloneManager repositoryCloneManager,
        ITelemetryRecorder telemetryRecorder,
        InitializationBackgroundServiceOptions options,
        JobScopeManager jobScopeManager)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using (ITelemetryScope scope = telemetryRecorder.RecordGitOperation(TrackedGitOperation.Clone, options.VmrUri))
        {
            // If Vmr cloning is taking more than 20 min, something is wrong
            var linkedTokenSource = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, new CancellationTokenSource(TimeSpan.FromMinutes(20)).Token);

            ILocalGitRepo repo = await repositoryCloneManager.PrepareVmrAsync("main", linkedTokenSource.Token);
            linkedTokenSource.Token.ThrowIfCancellationRequested();

            scope.SetSuccess();
            jobScopeManager.InitializingDone();
        }
    }
}
