﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace ProductConstructionService.WorkItems;

public static class WorkItemProcessorStateInitialization
{
    public static async Task SetWorkItemProcessorInitialState(this WebApplication app)
    {
        var state = app.Services.GetRequiredService<WorkItemProcessorState>();

        if (app.Environment.IsDevelopment())
        {
            await state.SetStartAsync();
        }
        else
        {
            await state.SetInitializingAsync();
        }
    }
}
