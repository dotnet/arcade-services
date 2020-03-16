using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.DncEng.Configuration.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Mono.Options;

namespace Microsoft.DncEng.DeployServiceFabricCluster
{
    public static class Program
    {
        static async Task Main(string[] args)
        {
            string? configFile = null;
            string? environment = null;
            var options = new OptionSet
            {
                {"c|config=", c => configFile = c},
                {"e|environment=", e => environment = e},
            };

            List<string> remaining = options.Parse(args);
            if (remaining.Any())
            {
                Fatal(1, $"argument '{remaining[0]}' not recognized.");
            }

            RequireParameter(configFile, "--config");
            RequireParameter(environment, "--environment");

            if (!File.Exists(configFile))
            {
                Fatal(3, $"config file '{configFile}' does not exist.");
            }

            string? configFileName = Path.GetFileNameWithoutExtension(configFile);
            string? configExtension = Path.GetExtension(configFile);
            string? configDir = Path.GetDirectoryName(configFile);

            IConfigurationRoot configuration = new ConfigurationBuilder()
                .SetBasePath(Path.GetDirectoryName(configFile))
                .AddDefaultJsonConfiguration(new HostEnvironment(environment, configDir),
                    configFileName + "{0}" + configExtension)
                .Build();

            var config = new ServiceFabricClusterConfiguration();
            configuration.Bind(config);

            config.Validate();

            var services = new ServiceCollection();

            services.AddLogging(builder =>
            {
                builder.AddConsole();
            });

            await using ServiceProvider provider = services.BuildServiceProvider();

            ILoggerFactory loggerFactory = provider.GetRequiredService<ILoggerFactory>();
            ILogger logger = loggerFactory.CreateLogger("ClusterCreation");
            var creator = new ServiceFabricClusterCreator(config, logger);

            var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (_, __) => cts.Cancel();

            await creator.CreateClusterAsync(cts.Token);
        }

        private static void RequireParameter([NotNull] string? parameter, string name)
        {
            if (string.IsNullOrEmpty(parameter))
            {
                Fatal(2, $"the {name} parameter is required.");
            }
        }

        [DoesNotReturn]
        private static void Fatal(int code, string errorMessage)
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("BUILD_BUILDNUMBER")))
            {
                Console.Error.WriteLine("fatal: " + errorMessage);
            }
            else
            {
                Console.Error.WriteLine($"##vso[task.logissue type=error]{errorMessage}");
            }
            Environment.Exit(code);
        }
    }
}
