// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using CommandLine;
using Microsoft.Extensions.DependencyInjection;
using ProductConstructionService.Deployment;

return Parser.Default.ParseArguments<DeploymentOptions>(args)
    .MapResult((options) =>
    {
        async Task RunAsync()
        {
            IServiceCollection services = new ServiceCollection();

            await options.RegisterServices(services);

            var provider = services.BuildServiceProvider();

            var deployer = ActivatorUtilities.CreateInstance<Deployer>(provider);
            await deployer.DeployAsync();
        }

        RunAsync().GetAwaiter().GetResult();

        return 0;
    },
    (_) => -1);


