using Microsoft.Azure.Functions.Extensions.DependencyInjection;
using Microsoft.Azure.KeyVault;
using Microsoft.Azure.KeyVault.Models;
using Microsoft.Azure.Services.AppAuthentication;
using Microsoft.Azure.WebJobs;
using Microsoft.DotNet.Services.Utility;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.WindowsAzure.Storage.Table;
using RolloutScorer;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

[assembly: FunctionsStartup(typeof(RolloutScorerAzureFunction.Startup))]
namespace RolloutScorerAzureFunction
{
    public class KeyVaultOptions
    {
        public string Uri { get; set; }
    }

    public class GitHubOptions
    {
        public string PatSecretName { get; set; }
    }

    public class Startup : FunctionsStartup
    {
        public override void Configure(IFunctionsHostBuilder builder)
        {
            AzureServiceTokenProvider provider = new AzureServiceTokenProvider();
            KeyVaultClient kv = new KeyVaultClient(new KeyVaultClient.AuthenticationCallback(provider.KeyVaultTokenCallback));
            
            var config = new ConfigurationBuilder()
                .SetBasePath(builder.GetContext().ApplicationRootPath)
                .AddJsonFile("local.settings.json", optional: true, reloadOnChange: true)
                .AddEnvironmentVariables()
                .Build();

            builder.Services
                .AddLogging(l => l.AddConsole().AddFilter(l => l >= LogLevel.Information)) // TODO: get log level from host.json settings
                .AddSingleton(config)
                .Configure<KeyVaultOptions>(config.GetSection("KeyVault").Bind)
                .Configure<GitHubOptions>(config.GetSection("GitHub").Bind)
                .AddSingleton<IKeyVaultClient>(kv)
                .AddSingleton<ICloudTableFactory, CloudTableFactory>()
                .AddSingleton<RolloutScorerFunction>()
                .BuildServiceProvider();
        }
    }

    public class RolloutScorerFunction
    {
        private const int ScoringBufferInDays = 2;
        private readonly IKeyVaultClient _keyVaultClient;
        private readonly ILogger<RolloutScorerFunction> _logger;
        private readonly KeyVaultOptions _keyVaultOptions;
        private readonly GitHubOptions _gitHubOptions;
        private readonly ICloudTableFactory _cloudTableFactory;

        public RolloutScorerFunction(
            ILogger<RolloutScorerFunction> logger,
            IKeyVaultClient keyVaultClient, 
            IOptionsSnapshot<KeyVaultOptions> keyVaultOptions, 
            IOptionsSnapshot<GitHubOptions> gitHubOptions,
            ICloudTableFactory cloudTableFactory)
        {
            _logger = logger;
            _keyVaultClient = keyVaultClient;
            _keyVaultOptions = keyVaultOptions.Value;
            _gitHubOptions = gitHubOptions.Value;
            _cloudTableFactory = cloudTableFactory;
        }

        [FunctionName("RolloutScorerFunction")]
        public async Task Run([TimerTrigger("0 0 0 * * *", RunOnStartup = true)]TimerInfo myTimer)
        {
            string deploymentEnvironment = Environment.GetEnvironmentVariable("DeploymentEnvironment") ?? "Staging";
            _logger.LogInformation($"INFO: Deployment Environment: {deploymentEnvironment}");

            _logger.LogInformation("INFO: Getting storage account keys from KeyVault...");
            SecretBundle scorecardsStorageAccountKey = await _keyVaultClient.GetSecretAsync(_keyVaultOptions.Uri, ScorecardsStorageAccount.KeySecretName);
            SecretBundle deploymentTableSasToken = await _keyVaultClient.GetSecretAsync("https://DotNetEng-Status-Prod.vault.azure.net", "deployment-table-sas-token");

            _logger.LogInformation("INFO: Getting cloud tables...");
            CloudTable scorecardsTable = _cloudTableFactory.CreateScoreCardTable(scorecardsStorageAccountKey.Value);
            CloudTable deploymentsTable = _cloudTableFactory.CreateDeploymentTable($"https://dotnetengstatusprod.table.core.windows.net/deployments{deploymentTableSasToken.Value}");

            List<ScorecardEntity> scorecardEntries = await GetAllTableEntriesAsync<ScorecardEntity>(scorecardsTable);
            scorecardEntries.Sort((x, y) => x.Date.CompareTo(y.Date));
            List<AnnotationEntity> deploymentEntries = await GetAllTableEntriesAsync<AnnotationEntity>(deploymentsTable);
            deploymentEntries.Sort((x, y) => (x.Ended ?? DateTimeOffset.MaxValue).CompareTo(y.Ended ?? DateTimeOffset.MaxValue));
            _logger.LogInformation($"INFO: Found {scorecardEntries?.Count ?? -1} scorecard table entries and {deploymentEntries?.Count ?? -1} deployment table entries." + $"(-1 indicates that null was returned.)");

            // The deployments we care about are ones that occurred after the last scorecard

            // TODO: Change this line to fix the timing. Logic should be based on last day of a rollout (how do we determine what the last day of a rollout is?) rather than the last time it was scored. 
            IEnumerable<AnnotationEntity> relevantDeployments = deploymentEntries.Where(d => (d.Ended ?? DateTimeOffset.MaxValue) > scorecardEntries.Last().Date.AddDays(ScoringBufferInDays));
            _logger.LogInformation($"INFO: Found {relevantDeployments?.Count() ?? -1} relevant deployments (deployments which occurred " + $"after the last scorecard). (-1 indicates that null was returned.)");

            if (relevantDeployments.Count() > 0)
            {
                _logger.LogInformation("INFO: Checking to see if the most recent deployment occurred more than two days ago...");
                // We have only want to score if the buffer period has elapsed since the last deployment
                if ((relevantDeployments.Last().Ended ?? DateTimeOffset.MaxValue) < DateTimeOffset.UtcNow - TimeSpan.FromDays(ScoringBufferInDays))
                {
                    var scorecards = new List<Scorecard>();

                    _logger.LogInformation("INFO: Rollouts will be scored. Fetching GitHub PAT...");
                    SecretBundle githubPat = await _keyVaultClient.GetSecretAsync(_keyVaultOptions.Uri, _gitHubOptions.PatSecretName);
                    
                    // We'll score the deployments by service
                    foreach (var deploymentGroup in relevantDeployments.GroupBy(d => d.Service))
                    {
                        string currentRepo = deploymentGroup.Key;

                        _logger.LogInformation($"INFO: Scoring {deploymentGroup?.Count() ?? -1} rollouts for repo '{deploymentGroup.Key}'");

                        _logger.LogInformation($"INFO: Finding repo config for {currentRepo}...");
                        RepoConfig currentRepoConfig = Configs.DefaultConfig.RepoConfigs[currentRepo];
                        _logger.LogInformation($"INFO: Repo config: {currentRepoConfig.Repo}");

                        _logger.LogInformation($"INFO: Finding AzDO config for {currentRepoConfig.AzdoInstance}...");
                        AzdoInstanceConfig currentAzdoInstanceConfig = Configs.DefaultConfig.AzdoInstanceConfigs[currentRepoConfig.AzdoInstance];

                        RolloutScorer.RolloutScorer rolloutScorer = new RolloutScorer.RolloutScorer
                        {
                            Repo = deploymentGroup.Key,
                            RolloutStartDate = deploymentGroup.First().Started.GetValueOrDefault().Date,  // Is this going to blow up if GetValueOrDefault returns null?
                            RolloutWeightConfig = Configs.DefaultConfig.RolloutWeightConfig,
                            GithubConfig = Configs.DefaultConfig.GithubConfig,
                            Log = _logger,
                            RepoConfig = currentRepoConfig, 
                            AzdoConfig = currentAzdoInstanceConfig, 
                            KeyVaultClient = _keyVaultClient
                        };

                        _logger.LogInformation($"INFO: Fetching AzDO PAT from KeyVault...");
                        
                        rolloutScorer.SetupHttpClient(
                            (await _keyVaultClient.GetSecretAsync(rolloutScorer.AzdoConfig.KeyVaultUri, rolloutScorer.AzdoConfig.PatSecretName)).Value);
                        
                        rolloutScorer.SetupGithubClient(githubPat.Value);

                        _logger.LogInformation($"INFO: Attempting to initialize RolloutScorer...");
                        try
                        {
                            await rolloutScorer.InitAsync();
                        }
                        catch (ArgumentException e)
                        {
                            _logger.LogError($"ERROR: Error while processing {rolloutScorer.RolloutStartDate} rollout of {rolloutScorer.Repo}.");
                            _logger.LogError($"ERROR: {e.Message}");
                            continue;
                        }

                        _logger.LogInformation($"INFO: Creating rollout scorecard...");
                        scorecards.Add(await Scorecard.CreateScorecardAsync(rolloutScorer));
                        _logger.LogInformation($"INFO: Successfully created scorecard for {rolloutScorer.RolloutStartDate.Date} rollout of {rolloutScorer.Repo}.");
                    }

                    _logger.LogInformation($"INFO: Uploading results for {string.Join(", ", scorecards.Select(s => s.Repo))}");
                    await RolloutUploader.UploadResultsAsync(scorecards, Utilities.GetGithubClient(githubPat.Value),
                        scorecardsStorageAccountKey.Value, Configs.DefaultConfig.GithubConfig, skipPr: deploymentEnvironment != "Production");
                }
                else
                {
                    _logger.LogInformation(relevantDeployments.Last().Ended.HasValue ? $"INFO: Most recent rollout occurred less than two days ago " +
                        $"({relevantDeployments.Last().Service} on {relevantDeployments.Last().Ended.Value}); waiting to score." :
                        $"Most recent rollout ({relevantDeployments.Last().Service}) is still in progress.");
                }
            }
            else
            {
                _logger.LogInformation($"INFO: Found no rollouts which occurred after last recorded rollout " +
                    $"({(scorecardEntries.Count > 0 ? $"date {scorecardEntries.Last().Date}" : "no rollouts in table")})");
            }
        }

        private async Task<List<T>> GetAllTableEntriesAsync<T>(CloudTable table) where T : ITableEntity, new()
        {
            List<T> items = new List<T>();
            TableContinuationToken token = null;
            do
            {
                var queryResult = await table.ExecuteQuerySegmentedAsync(new TableQuery<T>(), token);
                foreach (var item in queryResult)
                {
                    items.Add(item);
                }
                token = queryResult.ContinuationToken;
            } while (token != null);
            return items;
        }
    }
}
