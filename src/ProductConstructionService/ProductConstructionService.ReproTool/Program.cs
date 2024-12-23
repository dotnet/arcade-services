// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using CommandLine;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ProductConstructionService.ReproTool;

Console.WriteLine("Hello, World!");

Parser.Default.ParseArguments<ReproToolOptions>(args)
    .WithParsed<ReproToolOptions>(o =>
    {
        ServiceCollection services = new ServiceCollection();

        services.AddSingleton(o);
        services.AddLogging();
        services.AddSingleton<ILogger>(sp => sp.GetRequiredService<ILogger<IProcessManager>>());

        services.AddSingleton<IBarApiClient>(sp => new BarApiClient(
            null,
            managedIdentityId: null,
            disableInteractiveAuth: false,
            "https://maestro.dot.net"));
        services.AddSingleton<IProcessManager>(sp => ActivatorUtilities.CreateInstance<ProcessManager>(sp, "git"));

        var provider = services.BuildServiceProvider();

        ActivatorUtilities.CreateInstance<ReproTool>(provider).ReproduceCodeFlow().GetAwaiter().GetResult();
    });
