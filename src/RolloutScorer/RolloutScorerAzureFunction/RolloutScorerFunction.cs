using Microsoft.Azure.KeyVault;
using Microsoft.Azure.KeyVault.Models;
using Microsoft.Azure.Services.AppAuthentication;
using Microsoft.Azure.WebJobs;
using Microsoft.DotNet.Services.Utility;
using Microsoft.Extensions.Logging;
using Microsoft.WindowsAzure.Storage.Table;
using RolloutScorer;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace RolloutScorerAzureFunction
{
    public static class RolloutScorerFunction
    {
        private const int ScoringBufferInDays = 2;

        [FunctionName("RolloutScorerFunction")]
        public static async Task Run([TimerTrigger("0 0 0 * * *")]TimerInfo myTimer, ILogger log)
        {
            AzureServiceTokenProvider tokenProvider = new AzureServiceTokenProvider();

            string deploymentEnvironment = Environment.GetEnvironmentVariable("DeploymentEnvironment") ?? "Staging";
            log.LogInformation($"Deployment Environment: {deploymentEnvironment}");

            log.LogInformation("Getting storage account keys from KeyVault...");
            SecretBundle scorecardsStorageAccountKey = await GetStorageAccountKeyAsync(tokenProvider,
                Utilities.KeyVaultUri, ScorecardsStorageAccount.KeySecretName);
            SecretBundle deploymentTableSasToken = await GetStorageAccountKeyAsync(tokenProvider,
                "https://DotNetEng-Status-Prod.vault.azure.net", "deployment-table-sas-token");

            log.LogInformation("Getting cloud tables...");
            CloudTable scorecardsTable = Utilities.GetScorecardsCloudTable(scorecardsStorageAccountKey.Value);
            CloudTable deploymentsTable = new CloudTable(
                new Uri($"https://dotnetengstatusprod.table.core.windows.net/deployments{deploymentTableSasToken.Value}"));

            List<ScorecardEntity> scorecardEntries =
                await GetAllTableEntriesAsync<ScorecardEntity>(scorecardsTable);
            scorecardEntries.Sort((x, y) => x.Date.CompareTo(y.Date));
            List<AnnotationEntity> deploymentEntries =
                await GetAllTableEntriesAsync<AnnotationEntity>(deploymentsTable);
            deploymentEntries.Sort((x, y) => (x.Ended ?? DateTimeOffset.MaxValue).CompareTo(y.Ended ?? DateTimeOffset.MaxValue));
            log.LogInformation($"Found {scorecardEntries?.Count ?? -1} scorecard table entries and {deploymentEntries?.Count ?? -1} deployment table entries." +
                $"(-1 indicates that null was returned.)");

            // The deployments we care about are ones that occurred after the last scorecard
            IEnumerable<AnnotationEntity> relevantDeployments =
                deploymentEntries.Where(d => (d.Ended ?? DateTimeOffset.MaxValue) > scorecardEntries.Last().Date);
            log.LogInformation($"Found {relevantDeployments?.Count() ?? -1} relevant deployments (deployments which occurred " +
                $"after the last scorecard). (-1 indicates that null was returned.)");

            if (relevantDeployments.Count() > 0)
            {
                log.LogInformation("Checking to see if the most recent deployment occurred more than two days ago...");
                // We have only want to score if the buffer period has elapsed since the last deployment
                if ((relevantDeployments.Last().Ended ?? DateTimeOffset.MaxValue) < DateTimeOffset.UtcNow - TimeSpan.FromDays(0))
                {
                    var scorecards = new List<Scorecard>();

                    log.LogInformation("Rollouts will be scored. Fetching GitHub PAT...");
                    SecretBundle githubPat;
                    using (KeyVaultClient kv = new KeyVaultClient(new KeyVaultClient.AuthenticationCallback(tokenProvider.KeyVaultTokenCallback)))
                    {
                        githubPat = await kv.GetSecretAsync(Utilities.KeyVaultUri, Utilities.GitHubPatSecretName);
                    }

                    // We'll score the deployments by service
                    foreach (var deploymentGroup in relevantDeployments.GroupBy(d => d.Service))
                    {
                        log.LogInformation($"Scoring {deploymentGroup?.Count() ?? -1} rollouts for repo '{deploymentGroup.Key}'");
                        RolloutScorer.RolloutScorer rolloutScorer = new RolloutScorer.RolloutScorer
                        {
                            Repo = deploymentGroup.Key,
                            RolloutStartDate = deploymentGroup.First().Started.GetValueOrDefault().Date,
                            RolloutWeightConfig = Configs.DefaultConfig.RolloutWeightConfig,
                            GithubConfig = Configs.DefaultConfig.GithubConfig,
                            Log = log,
                        };
                        log.LogInformation($"Finding repo config for {rolloutScorer.Repo}...");
                        rolloutScorer.RepoConfig = Configs.DefaultConfig.RepoConfigs
                            .Find(r => r.Repo == rolloutScorer.Repo);
                        log.LogInformation($"Repo config: {rolloutScorer.RepoConfig.Repo}");
                        log.LogInformation($"Finding AzDO config... for {rolloutScorer.RepoConfig.AzdoInstance}");
                        rolloutScorer.AzdoConfig = Configs.DefaultConfig.AzdoInstanceConfigs
                            .Find(a => a.Name == rolloutScorer.RepoConfig.AzdoInstance);
                        log.LogInformation($"AzdoConfig: {rolloutScorer.AzdoConfig.Name}");

                        log.LogInformation($"Fetching AzDO PAT from KeyVault...");
                        using (KeyVaultClient kv = new KeyVaultClient(new KeyVaultClient.AuthenticationCallback(tokenProvider.KeyVaultTokenCallback)))
                        {
                            rolloutScorer.SetupHttpClient(
                                (await kv.GetSecretAsync(rolloutScorer.AzdoConfig.KeyVaultUri, rolloutScorer.AzdoConfig.PatSecretName)).Value);
                        }
                        rolloutScorer.SetupGithubClient(githubPat.Value);

                        log.LogInformation($"Attempting to initialize RolloutScorer...");
                        try
                        {
                            await rolloutScorer.InitAsync();
                        }
                        catch (ArgumentException e)
                        {
                            log.LogError($"Error while processing {rolloutScorer.RolloutStartDate} rollout of {rolloutScorer.Repo}.");
                            log.LogError(e.Message);
                            continue;
                        }

                        log.LogInformation($"Creating rollout scorecard...");
                        scorecards.Add(await Scorecard.CreateScorecardAsync(rolloutScorer));
                        log.LogInformation($"Successfully created scorecard for {rolloutScorer.RolloutStartDate.Date} rollout of {rolloutScorer.Repo}.");
                    }

                    log.LogInformation($"Uploading results for {string.Join(", ", scorecards.Select(s => s.Repo))}");
                    await RolloutUploader.UploadResultsAsync(scorecards, Utilities.GetGithubClient(githubPat.Value),
                        scorecardsStorageAccountKey.Value, Configs.DefaultConfig.GithubConfig, skipPr: deploymentEnvironment == "Production");
                }
                else
                {
                    log.LogInformation(relevantDeployments.Last().Ended.HasValue ? $"Most recent rollout occurred less than two days ago " +
                        $"({relevantDeployments.Last().Service} on {relevantDeployments.Last().Ended.Value}); waiting to score." :
                        $"Most recent rollout ({relevantDeployments.Last().Service}) is still in progress.");
                }
            }
            else
            {
                log.LogInformation($"Found no rollouts which occurred after last recorded rollout (date {scorecardEntries.Last().Date})");
            }
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

        private static async Task<SecretBundle> GetStorageAccountKeyAsync(AzureServiceTokenProvider tokenProvider, string keyVaultUri, string secretName)
        {
            using (KeyVaultClient kv = new KeyVaultClient(new KeyVaultClient.AuthenticationCallback(tokenProvider.KeyVaultTokenCallback)))
            {
                return await kv.GetSecretAsync(keyVaultUri, secretName);
            }
        }
    }
}
