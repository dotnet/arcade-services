﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.


using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using ProductConstructionService.Api.Telemetry;

namespace ProductConstructionService.Api.VirtualMonoRepo;

public class VmrCloneStartupFilter(
    IRepositoryCloneManager repositoryCloneManager,
    ITelemetryRecorder telemetryRecorder,
    VmrCloneStartupFilterOptions options) : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
    {
        return builder =>
        {
            using (ITelemetryScope scope = telemetryRecorder.RecordGitClone(options.VmrUri))
            {
                // If Vmr cloning is taking more than 20 min, something is wrong
                CancellationTokenSource tokenSource = new(TimeSpan.FromMinutes(20));
                ILocalGitRepo repo = repositoryCloneManager.PrepareVmrCloneAsync(options.VmrUri, tokenSource.Token).GetAwaiter().GetResult();
                tokenSource.Token.ThrowIfCancellationRequested();

                scope.SetSuccess();
            }

            next(builder);
        };
    }
}
