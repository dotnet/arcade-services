// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using CommandLine;
using FlatFlowMigrationCli.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

Type[] options =
[
    typeof(MigrateOptions),
    typeof(RollbackOptions),
];

return Parser.Default.ParseArguments(args, options)
    .MapResult((Options options) =>
    {
        async Task<int> RunAsync()
        {
            IServiceCollection services = new ServiceCollection();

            await options.RegisterServices(services);

            var provider = services.BuildServiceProvider();
            var operation = options.GetOperation(provider);

            try
            {
                return await operation.RunAsync();
            }
            catch (Exception e)
            {
                var logger = provider.GetRequiredService<ILogger<Program>>();
                logger.LogError(e.Message);
                logger.LogDebug(e.ToString());
                return 1;
            }
        }

        return RunAsync().Result;
    },
    (_) => -1);
