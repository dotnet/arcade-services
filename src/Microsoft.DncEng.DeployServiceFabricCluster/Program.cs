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
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Mono.Options;

namespace Microsoft.DncEng.DeployServiceFabricCluster
{
    class HostEnvironment : IHostEnvironment
    {
        public HostEnvironment(string environmentName, string contentRootPath)
        {
            EnvironmentName = environmentName;
            ContentRootPath = contentRootPath;
        }

        public string EnvironmentName { get; set; }
        public string ApplicationName { get; set; }
        public string ContentRootPath { get; set; }
        public IFileProvider ContentRootFileProvider { get; set; }
    }

    class Program
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

            var remaining = options.Parse(args);
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

            var configFileName = Path.GetFileNameWithoutExtension(configFile);
            var configExtension = Path.GetExtension(configFile);
            var configDir = Path.GetDirectoryName(configFile);

            var configuration = new ConfigurationBuilder()
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

            await using var provider = services.BuildServiceProvider();

            var loggerFactory = provider.GetRequiredService<ILoggerFactory>();
            var logger = loggerFactory.CreateLogger("ClusterCreation");
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
            Console.Error.WriteLine("fatal: " + errorMessage);
            Environment.Exit(code);
        }
    }

    public class ServiceFabricClusterConfiguration
    {
        public void Validate()
        {
            if (NodeTypes.All(nt => nt.Name != "Primary"))
            {
                throw new ArgumentException("Must have a single node type named 'Primary'");
            }
            NodeTypes = NodeTypes.OrderBy(nt => nt.Name == "Primary" ? 0 : 1).ToList();
        }

        public string Name { get; set; }

        public string Location { get; set; }

        public string ResourceGroup { get; set; }

        public Guid SubscriptionId { get; set; }

        public string AdminUsername { get; set; }
        public string AdminPassword { get; set; }

        public int TcpGatewayPort { get; set; } = 19000;
        public int HttpGatewayPort { get; set; } = 19080;

        public List<ServiceFabricNodeType> NodeTypes { get; set; }
        public string CertificateCommonName { get; set; }
        public string AdminClientCertificateCommonName { get; set; }
        public string AdminClientCertificateIssuerThumbprint { get; set; }
        public string CertificateSourceVaultId { get; set; }
        public List<string> CertificateUrls { get; set; }
    }

    public class ServiceFabricNodeType
    {
        public string Name { get; set; }

        public List<ServiceFabricNodeEndpoint> Endpoints { get; set; }
        public int InstanceCount { get; set; }
        public string UserAssignedIdentityId { get; set; }
        public string Sku { get; set; }
        public ServiceFabricNodeVmImage VmImage { get; set; }
    }

    public class ServiceFabricNodeVmImage
    {
        public string Publisher { get; set; }
        public string Offer { get; set; }
        public string Sku { get; set; }
        public string Version { get; set; }
    }

    public class ServiceFabricNodeEndpoint
    {
        public int ExternalPort { get; set; }
        public int InternalPort { get; set; }
    }
}
