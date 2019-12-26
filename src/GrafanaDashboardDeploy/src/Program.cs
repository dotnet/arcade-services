using Microsoft.Extensions.Logging;
using Mono.Options;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;

namespace DotNet.Grafana
{
    class Program
    {
        static async Task Main(string[] args)
        {
            var loggerFactory = LoggerFactory.Create(builder =>
            {
                builder.SetMinimumLevel(LogLevel.Trace);
                builder.AddConsole();
            });
            ILogger logger = loggerFactory.CreateLogger<Program>();

            var httpClient = new HttpClient();
            var grafanaClient = new GrafanaClient(loggerFactory.CreateLogger<GrafanaClient>(), httpClient, "http://localhost:3000");

            CommandSet commands = new CommandSet("grafana-client")
                //.Add(new Command("get-health", "Get service health") { Run = _ => { PrintHealth(grafanaClient).Wait(); } })
                .Add(new PrintHealthCommand(grafanaClient, "get-health", "Get the health of the Grafana instance"))
                .Add(new MakeExportPackCommand(grafanaClient, "make-pack", "Make the export pack"))
                .Add(new PostExportPackCommand(grafanaClient, "post-pack", "Post the export pack"));

            commands.Run(args);
        }

        private class PrintHealthCommand : Command
        {
            private readonly GrafanaClient grafanaClient;

            public PrintHealthCommand(GrafanaClient grafanaClient, string name, string help = null) : base(name, help)
            {
                this.grafanaClient = grafanaClient;

                Options = new OptionSet()
                {
                    { "api-host=", "URL to the Grafana instance", v => grafanaClient.BaseUrl = v },
                };

                Run = _ =>
                {
                    var health = grafanaClient.GetHealthAsync().Result;
                    Console.WriteLine($"Health: {health.Database}, version: {health.Version} (commit: {health.Commit})");
                };
            }
        }

        private class MakeExportPackCommand : Command
        {
            private readonly List<string> dashboardUids = new List<string>();
            private string outputFilePath;
            private readonly GrafanaClient grafanaClient;

            public MakeExportPackCommand(GrafanaClient grafanaClient, string name, string help = null) : base(name, help)
            {
                this.grafanaClient = grafanaClient;

                Options = new OptionSet()
                {
                    { "api-host=", "URL to the Grafana instance", v => grafanaClient.BaseUrl = v },
                    { "api-token=", "A Grafana API Token", v => grafanaClient.Credentials = new Credentials(v) },
                    { "dashboard-uid=", "dashboard UIDs, may specify more than one", v => dashboardUids.Add(v) },
                    { "output-file=", "path to the output file", v => outputFilePath = v }
                };

                Run = _ => DeployTool.MakeExportPack(this.grafanaClient, dashboardUids, outputFilePath).Wait();
            }
        }

        private class PostExportPackCommand : Command
        {
            private string inputFilePath;
            private readonly GrafanaClient grafanaClient;

            public PostExportPackCommand(GrafanaClient grafanaClient, string name, string help = null) : base(name, help)
            {
                this.grafanaClient = grafanaClient;

                Options = new OptionSet()
                {
                    { "api-host=", "URL to the Grafana instance", v => grafanaClient.BaseUrl = v },
                    { "api-token=", "A Grafana API Token", v => grafanaClient.Credentials = new Credentials(v) },
                    { "input-file=", "path to the input file", v => inputFilePath = v }
                };

                Run = _ => DeployTool.PostExportPack(grafanaClient, inputFilePath).Wait();
            }
        }
    }
}
