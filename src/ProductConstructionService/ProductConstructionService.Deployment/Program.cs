// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using CommandLine;
using Microsoft.Extensions.DependencyInjection;
using ProductConstructionService.Deployment;

return Parser.Default.ParseArguments<DeploymentOptions>(args)
    .MapResult((options) =>
    {
        IServiceCollection services = new ServiceCollection();

        options.RegisterServices(services).GetAwaiter().GetResult();

        var provider = services.BuildServiceProvider();

        var deployer = ActivatorUtilities.CreateInstance<Deployer>(provider);
        return deployer.DeployAsync().GetAwaiter().GetResult();
    },
    (_) => -1);


