// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.Extensions.DependencyInjection;

IServiceCollection serviceCollection = new ServiceCollection();

VersionPropsFormatter.VersionPropsFormatter.RegisterServices(serviceCollection);

IServiceProvider serviceProvider = serviceCollection.BuildServiceProvider();

try
{
    await ActivatorUtilities.CreateInstance<VersionPropsFormatter.VersionPropsFormatter>(serviceProvider).RunAsync(Directory.GetCurrentDirectory());
}
finally
{
    // Ensure all logs are flushed by properly disposing the service provider
    if (serviceProvider is IDisposable disposable)
    {
        disposable.Dispose();
    }
    else if (serviceProvider is IAsyncDisposable asyncDisposable)
    {
        await asyncDisposable.DisposeAsync();
    }
}
