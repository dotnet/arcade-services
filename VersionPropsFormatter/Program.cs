// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Maestro.Common;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

IServiceCollection serviceCollection = new ServiceCollection();

RegisterServices(serviceCollection);

IServiceProvider serviceProvider = serviceCollection.BuildServiceProvider();

try
{
    await ActivatorUtilities.CreateInstance<VersionPropsFormatter.VersionPropsFormatter>(serviceProvider).RunAsync();
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


IServiceCollection RegisterServices(IServiceCollection services)
{
    services.AddLogging(options => options.AddConsole());
    services.AddSingleton<ILogger>(sp => sp.GetRequiredService<ILogger<IProcessManager>>());
    services.AddSingleton<ITelemetryRecorder, NoTelemetryRecorder>();
    services.AddSingleton<IRemoteTokenProvider>(new RemoteTokenProvider());
    services.AddSingleton<IFileSystem, FileSystem>();
    services.AddSingleton<IGitRepo, LocalLibGit2Client>();
    services.AddSingleton<IDependencyFileManager, DependencyFileManager>();

    services.AddSingleton<IProcessManager>(sp => ActivatorUtilities.CreateInstance<ProcessManager>(sp, "git"));
    services.AddSingleton<IVersionDetailsParser, VersionDetailsParser>();
    return services;
}
