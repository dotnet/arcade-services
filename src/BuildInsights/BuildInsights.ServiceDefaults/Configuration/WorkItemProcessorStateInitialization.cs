// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ProductConstructionService.WorkItems;

namespace BuildInsights.ServiceDefaults.Configuration;

public static class WorkItemProcessorStateInitialization
{
    public static async Task SetWorkItemProcessorInitialState(this IHost app)
    {
        var state = app.Services.GetRequiredService<WorkItemProcessorState>();

        if (app.Services.GetRequiredService<IHostEnvironment>().IsDevelopment())
        {
            await state.SetStartAsync();
        }
        else
        {
            await state.SetInitializingAsync();
        }
    }
}
