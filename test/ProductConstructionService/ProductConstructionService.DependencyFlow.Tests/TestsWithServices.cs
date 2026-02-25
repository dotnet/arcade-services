// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.DotNet.Internal.Testing.Utility;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ProductConstructionService.DependencyFlow.Tests;

public abstract class TestsWithServices : TestsWithMocks
{
    protected virtual void RegisterServices(IServiceCollection services)
    {
    }

    protected virtual Task BeforeExecute(IServiceProvider serviceScope)
    {
        return Task.CompletedTask;
    }

    protected async Task Execute(Func<IServiceProvider, Task> run)
    {
        var services = new ServiceCollection();
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "XUNIT");
        services.AddLogging(l => l.AddProvider(new NUnitLogger()));
        RegisterServices(services);
        using (ServiceProvider container = services.BuildServiceProvider())
        using (IServiceScope scope = container.GetRequiredService<IServiceScopeFactory>().CreateScope())
        {
            await BeforeExecute(scope.ServiceProvider);
            await run(scope.ServiceProvider);
        }
    }
}
