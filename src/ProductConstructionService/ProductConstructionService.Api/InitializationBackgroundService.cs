﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using ProductConstructionService.Api.Queue;

namespace ProductConstructionService.Api;

internal class InitializationBackgroundService(
        IServiceScopeFactory serviceScopeFactory,
        ITelemetryRecorder telemetryRecorder,
        InitializationBackgroundServiceOptions options,
        JobScopeManager jobScopeManager)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using (IServiceScope scope = serviceScopeFactory.CreateScope())
        using (ITelemetryScope telemetryScope = telemetryRecorder.RecordGitOperation(TrackedGitOperation.Clone, options.VmrUri))
        {
            // If Vmr cloning is taking more than 20 min, something is wrong
            var linkedTokenSource = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, new CancellationTokenSource(TimeSpan.FromMinutes(20)).Token);

            IVmrCloneManager vmrCloneManager = scope.ServiceProvider.GetRequiredService<IVmrCloneManager>();
            await vmrCloneManager.PrepareVmrAsync("main", linkedTokenSource.Token);
            linkedTokenSource.Token.ThrowIfCancellationRequested();

            telemetryScope.SetSuccess();
            jobScopeManager.InitializingDone();
        }
    }
}
