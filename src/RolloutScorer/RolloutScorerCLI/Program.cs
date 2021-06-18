using Microsoft.Extensions.DependencyInjection;
using Mono.Options;
using Octokit;
using RolloutScorer;
using RolloutScorer.Services;
using RolloutScorer.Providers;
using System.Net.Http.Headers;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.DotNet.GitHub.Authentication;
using Microsoft.DotNet.Internal.AzureDevOps;

namespace RolloutScorerCLI
{
    public class Program
    {
        public static async Task<int> Main(string[] args)
        {
            var collection = new ServiceCollection();

            (string assemblyName, string assemblyVersion) = GetAssemblyVersion();

            //collection.AddSingleton<IGitHubClient>(c =>
            //        new GitHubClient(new Octokit.ProductHeaderValue(assemblyName, assemblyVersion))
            //        {
            //            //Credentials = new Credentials("buildanalysis", gitHubGistPat)
            //        });

            ConfigureServices(collection);

            await using ServiceProvider services = collection.BuildServiceProvider();

            var commands = new CommandSet("RolloutScorer")
            {
                "usage: RolloutScorer COMMAND [OPTIONS]",
                "",
                "Available commands:",
                services.GetRequiredService<ScoreCommand>(), //new ScoreCommand(),
                services.GetRequiredService<UploadCommand>(), //new UploadCommand(),
            };

            return commands.Run(args);
        }

        public static void ConfigureServices(IServiceCollection collection)
        {
            collection.AddSingleton<ScoreCommand>();
            collection.AddSingleton<UploadCommand>();
            collection.AddSingleton<RolloutUploader>();
            collection.AddLogging();
            collection.AddSingleton<IScorecardService, ScorecardProvider>();
            collection.AddSingleton<IRolloutScorerService, RolloutScorerProvider>();
            collection.AddSingleton<IIssueService, IssueProvider>();

            collection.AddSingleton<IGitHubClientFactory, GitHubClientFactory>();
            collection.AddSingleton<IAzureDevOpsClientFactory, AzureDevOpsClientFactory>();
        }

        public static ProductInfoHeaderValue GetProductInfoHeaderValue()
        {
            return new ProductInfoHeaderValue(typeof(Program).Assembly.GetName().Name,
                typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>().InformationalVersion);
        }

        private static (string assemblyName, string assemblyVersion) GetAssemblyVersion()
        {
            string assemblyName = Assembly.GetEntryAssembly()?.GetName().Name ?? "RolloutScorerCLI";
            string assemblyVersion =
                Assembly.GetEntryAssembly()
                    ?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                    ?.InformationalVersion ??
                "42.42.42.42";
            return (assemblyName, assemblyVersion);
        }
    }
}
