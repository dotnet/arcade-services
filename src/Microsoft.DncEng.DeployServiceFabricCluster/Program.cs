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
using Microsoft.Rest.Azure;
using Mono.Options;

namespace Microsoft.DncEng.DeployServiceFabricCluster
{
    public static class Program
    {
        static async Task Main(string[] args)
        {
            try
            {
                string? configFile = null;
                string? environment = null;
                string? deploy = null;
                var options = new OptionSet
                {
                    {"c|config=", c => configFile = c},
                    {"e|environment=", e => environment = e},
                    {"d|deploy=", d => deploy = d}
                };

                List<string> remaining = options.Parse(args);
                if (remaining.Any())
                {
                    Fatal(1, $"argument '{remaining[0]}' not recognized.");
                }

                RequireParameter(configFile, "--config");
                RequireParameter(environment, "--environment");
                RequireParameter(deploy, "--deploy");

                if (!File.Exists(configFile))
                {
                    Fatal(3, $"config file '{configFile}' does not exist.");
                }

                string? configFileName = Path.GetFileNameWithoutExtension(configFile);
                string? configExtension = Path.GetExtension(configFile);
                string? configDir = Path.GetDirectoryName(configFile);

                IConfigurationRoot configuration = new ConfigurationBuilder()
                    .SetBasePath(Path.GetDirectoryName(configFile))
                    .AddDefaultJsonConfiguration(new HostEnvironment(environment, configDir!),
                        configFileName + "{0}" + configExtension)
                    .Build();

                switch (deploy.ToLowerInvariant())
                {
                    case "gateway":
                        await RunDeploy<GatewayDeployer, GatewaySettings>(configuration);
                        break;
                    case "cluster":
                        await RunDeploy<ClusterDeployer, ClusterSettings>(configuration);
                        break;
                }
            }
            catch (CloudException ex)
            {
                PrintCloudError(ex.Body);
                Fatal(-1, "Error deploying, see above for details.");
            }
        }

        private static void PrintCloudError(CloudError error, string indent = "")
        {
            Console.Error.WriteLine(indent + error.Message);
            foreach (var e in error.Details)
            {
                PrintCloudError(e, indent + "  ");
            }
        }

        private static async Task RunDeploy<T, TSettings>(IConfiguration configuration)
            where T : ResourceGroupDeployer<string, TSettings>
            where TSettings : ResourceGroupDeployerSettings, new()
        {
            var config = new TSettings();
            configuration.Bind(config);
            config.Validate();

            var services = new ServiceCollection();
            services.AddLogging(builder =>
            {
                builder.AddConsole();
            });
            services.AddSingleton<T>();
            await using ServiceProvider provider = services.BuildServiceProvider();
            var deployer = ActivatorUtilities.CreateInstance<T>(provider, config, configuration);
            var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (_, __) => cts.Cancel();
            var result = await deployer.DeployAsync(cts.Token);
            Console.WriteLine(result);
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
