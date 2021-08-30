using Microsoft.Azure.KeyVault;
using Microsoft.Azure.KeyVault.Models;
using Microsoft.Azure.Services.AppAuthentication;
using Microsoft.DotNet.Services.Utility;
using Microsoft.Extensions.Logging;
using Microsoft.WindowsAzure.Storage.Table;
using RolloutScorer;
using Models = RolloutScorer.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.DotNet.ServiceFabric.ServiceHost;
using System.Threading;
using RolloutScorer.Services;

namespace RolloutScorer.Service
{
    public class RolloutScorerProcessor : IServiceImplementation
    {
        private readonly ILogger<RolloutScorerProcessor> _logger;
        private readonly IScorecardService _scorecardService;
        private readonly IRolloutScorerService _rolloutScorerService;
        private const int ScoringBufferInDays = 2;

        public RolloutScorerProcessor(ILogger<RolloutScorerProcessor> logger,
            IScorecardService scorecardService,
            IRolloutScorerService rolloutScorerService)
        {
            _logger = logger;
            _scorecardService = scorecardService;
            _rolloutScorerService = rolloutScorerService;
        }

        // public static async Task Run([TimerTrigger("0 0 0 * * *")] TimerInfo myTimer, ILogger log)
        public async Task<TimeSpan> RunAsync(CancellationToken cancellationToken)
        {
            AzureServiceTokenProvider tokenProvider = new AzureServiceTokenProvider();

            string deploymentEnvironment = Environment.GetEnvironmentVariable("DeploymentEnvironment") ?? "Staging";
            _logger.LogInformation($"INFO: Deployment Environment: {deploymentEnvironment}");

            _logger.LogInformation("INFO: Getting scorecard storage account key and deployment table's SAS URI from KeyVault...");
            SecretBundle scorecardsStorageAccountKey = await GetSecretBundleFromKeyVaultAsync(tokenProvider,
                Utilities.KeyVaultUri, ScorecardsStorageAccount.KeySecretName);
            SecretBundle deploymentTableSasUriBundle = await GetSecretBundleFromKeyVaultAsync(tokenProvider,
                "https://DotNetEng-Status-Prod.vault.azure.net", "deployment-table-sas-uri");

            _logger.LogInformation("INFO: Getting cloud tables...");
            CloudTable scorecardsTable = Utilities.GetScorecardsCloudTable(scorecardsStorageAccountKey.Value);
            CloudTable deploymentsTable = new CloudTable(new Uri(deploymentTableSasUriBundle.Value));

            List<ScorecardEntity> scorecardEntries = await GetAllTableEntriesAsync<ScorecardEntity>(scorecardsTable);
            scorecardEntries.Sort((x, y) => x.Date.CompareTo(y.Date));
            List<AnnotationEntity> deploymentEntries =
                await GetAllTableEntriesAsync<AnnotationEntity>(deploymentsTable);
            deploymentEntries.Sort((x, y) => (x.Ended ?? DateTimeOffset.MaxValue).CompareTo(y.Ended ?? DateTimeOffset.MaxValue));
            _logger.LogInformation($"INFO: Found {scorecardEntries?.Count ?? -1} scorecard table entries and {deploymentEntries?.Count ?? -1} deployment table entries." +
                $"(-1 indicates that null was returned.)");

            // The deployments we care about are ones that occurred after the last scorecard
            IEnumerable<AnnotationEntity> relevantDeployments =
                deploymentEntries.Where(d => (d.Ended ?? DateTimeOffset.MaxValue) > scorecardEntries.Last().Date.AddDays(ScoringBufferInDays));
            _logger.LogInformation($"INFO: Found {relevantDeployments?.Count() ?? -1} relevant deployments (deployments which occurred " +
                $"after the last scorecard). (-1 indicates that null was returned.)");

            if (relevantDeployments.Count() > 0)
            {
                _logger.LogInformation("INFO: Checking to see if the most recent deployment occurred more than two days ago...");
                // We have only want to score if the buffer period has elapsed since the last deployment
                if ((relevantDeployments.Last().Ended ?? DateTimeOffset.MaxValue) < DateTimeOffset.UtcNow - TimeSpan.FromDays(ScoringBufferInDays))
                {
                    var scorecards = new List<Models.Scorecard>();

                    _logger.LogInformation("INFO: Rollouts will be scored. Fetching GitHub PAT...");
                    SecretBundle githubPat;
                    using (KeyVaultClient kv = new KeyVaultClient(new KeyVaultClient.AuthenticationCallback(tokenProvider.KeyVaultTokenCallback)))
                    {
                        githubPat = await kv.GetSecretAsync(Utilities.KeyVaultUri, Utilities.GitHubPatSecretName);
                    }

                    // We'll score the deployments by service
                    foreach (var deploymentGroup in relevantDeployments.GroupBy(d => d.Service))
                    {
                        _logger.LogInformation($"INFO: Scoring {deploymentGroup?.Count() ?? -1} rollouts for repo '{deploymentGroup.Key}'");
                        Models.RolloutScorer rolloutScorer = new Models.RolloutScorer
                        {
                            Repo = deploymentGroup.Key,
                            RolloutStartDate = deploymentGroup.First().Started.GetValueOrDefault().Date,
                            RolloutWeightConfig = StandardConfig.DefaultConfig.RolloutWeightConfig,
                            GithubConfig = StandardConfig.DefaultConfig.GithubConfig,
                            Log = _logger,
                        };
                        _logger.LogInformation($"INFO: Finding repo config for {rolloutScorer.Repo}...");
                        rolloutScorer.RepoConfig = StandardConfig.DefaultConfig.RepoConfigs
                            .Find(r => r.Repo == rolloutScorer.Repo);
                        _logger.LogInformation($"INFO: Repo config: {rolloutScorer.RepoConfig.Repo}");
                        _logger.LogInformation($"INFO: Finding AzDO config for {rolloutScorer.RepoConfig.AzdoInstance}...");
                        rolloutScorer.AzdoConfig = StandardConfig.DefaultConfig.AzdoInstanceConfigs
                            .Find(a => a.Name == rolloutScorer.RepoConfig.AzdoInstance);

                        _logger.LogInformation($"INFO: Fetching AzDO PAT from KeyVault...");
                        using (KeyVaultClient kv = new KeyVaultClient(new KeyVaultClient.AuthenticationCallback(tokenProvider.KeyVaultTokenCallback)))
                        {
                            rolloutScorer.SetupHttpClient(
                                (await kv.GetSecretAsync(rolloutScorer.AzdoConfig.KeyVaultUri, rolloutScorer.AzdoConfig.PatSecretName)).Value);
                        }
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
                        scorecards.Add(await _scorecardService.CreateScorecardAsync(rolloutScorer));
                        _logger.LogInformation($"INFO: Successfully created scorecard for {rolloutScorer.RolloutStartDate.Date} rollout of {rolloutScorer.Repo}.");
                    }

                    _logger.LogInformation($"INFO: Uploading results for {string.Join(", ", scorecards.Select(s => s.Repo))}");
                    await RolloutUploader.UploadResultsAsync(scorecards,
                        scorecardsStorageAccountKey.Value, StandardConfig.DefaultConfig.GithubConfig, skipPr: deploymentEnvironment != "Production");
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

            return TimeSpan.Zero;
        }

        private static async Task<List<T>> GetAllTableEntriesAsync<T>(CloudTable table) where T : ITableEntity, new()
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

        private static async Task<SecretBundle> GetSecretBundleFromKeyVaultAsync(AzureServiceTokenProvider tokenProvider, string keyVaultUri, string secretName)
        {
            using (KeyVaultClient kv = new KeyVaultClient(new KeyVaultClient.AuthenticationCallback(tokenProvider.KeyVaultTokenCallback)))
            {
                return await kv.GetSecretAsync(keyVaultUri, secretName);
            }
        }
    }
}
