// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using CommandLine;
using Microsoft.Extensions.DependencyInjection;
using ProductConstructionService.ReproTool;

Parser.Default.ParseArguments<ReproToolOptions>(args)
    .WithParsed<ReproToolOptions>(o =>
    {
        ServiceCollection services = new ServiceCollection();

        services.RegisterServices(o);

        var provider = services.BuildServiceProvider();

        ActivatorUtilities.CreateInstance<ReproTool>(provider).ReproduceCodeFlow().GetAwaiter().GetResult();
    });
