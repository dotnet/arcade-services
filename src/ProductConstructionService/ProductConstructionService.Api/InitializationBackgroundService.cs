// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Maestro.Common.Telemetry;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using ProductConstructionService.WorkItems;

namespace ProductConstructionService.Api;

internal record InitializationBackgroundServiceOptions(string VmrUri);

/// <summary>
/// This service is responsible for initializing the VMR (clones it to the local disk).
/// </summary>
internal class InitializationBackgroundService(
        IServiceScopeFactory serviceScopeFactory,
        ITelemetryRecorder telemetryRecorder,
        InitializationBackgroundServiceOptions options,
        WorkItemScopeManager workItemScopeManager)
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
            await vmrCloneManager.PrepareVmrAsync([options.VmrUri], ["main"], "main", resetToRemote: true, linkedTokenSource.Token);
            linkedTokenSource.Token.ThrowIfCancellationRequested();

            telemetryScope.SetSuccess();
            await workItemScopeManager.InitializationFinished();
        }
    }
}
