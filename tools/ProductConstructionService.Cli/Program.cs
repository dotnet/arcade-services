// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using CommandLine;
using Microsoft.Extensions.DependencyInjection;
using ProductConstructionService.Cli.Options;

return Parser.Default.ParseArguments(args, GetOptions())
    .MapResult((Options options) =>
    {
        async Task<int> RunAsync()
        {
            IServiceCollection services = new ServiceCollection();

            await options.RegisterServices(services);

            var provider = services.BuildServiceProvider();

            var operation = options.GetOperation(provider);
            return await operation.RunAsync();
        }

        return RunAsync().GetAwaiter().GetResult();
    },
    (_) => -1);

Type[] GetOptions() =>
    [
        typeof(DeploymentOptions),
        typeof(GetPcsStatusOptions),
        typeof(StartPcsOptions),
        typeof(StopPcsOptions),
        typeof(FeatureFlagSetOptions),
        typeof(FeatureFlagGetOptions),
        typeof(FeatureFlagRemoveOptions),
        typeof(FeatureFlagListOptions),
        typeof(FeatureFlagAvailableOptions),
        typeof(ExportConfigurationOptions),
    ];
