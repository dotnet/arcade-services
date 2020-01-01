using DotNet.Status.Web.Controllers;
using Microsoft.Azure.KeyVault;
using Microsoft.Azure.KeyVault.Models;
using Microsoft.Azure.Services.AppAuthentication;
using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Logging;
using Microsoft.WindowsAzure.Storage;
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
        [FunctionName("RolloutScorerFunction")]
        public static async Task Run([TimerTrigger("0 0 0 * * *")]TimerInfo myTimer, ILogger log)
        {
            AzureServiceTokenProvider tokenProvider = new AzureServiceTokenProvider();
            SecretBundle scorecardsStorageAccountKey = await GetStorageAccountKeyAsync(tokenProvider, RolloutScorer.Utilities.KeyVaultUri, RolloutScorer.Utilities.StorageAccountKeySecretName);
            SecretBundle deploymentTableSasToken = await GetStorageAccountKeyAsync(tokenProvider, "https://DotNetEng-Status-Prod.vault.azure.net", "deployment-table-sas-token");
            CloudTable scorecardsTable = RolloutScorer.Utilities.GetScorecardsCloudTable(scorecardsStorageAccountKey);
            //CloudTable deploymentsTable = new CloudTable(new Uri($"https://dotnetengstatusprod.table.core.windows.net/deployments{deploymentTableSasToken.Value}"));
            CloudStorageAccount storageAccount = CloudStorageAccount.Parse(
                connectionString: $"DefaultEndpointsProtocol=https;AccountName={RolloutScorer.Utilities.StorageAccountName};AccountKey={scorecardsStorageAccountKey.Value};EndpointSuffix=core.windows.net");
            CloudTableClient tableClient = storageAccount.CreateCloudTableClient();
            CloudTable deploymentsTable = tableClient.GetTableReference("testdeployments");

            List<RolloutUploader.ScorecardEntity> scorecardEntries = await GetAllTableEntriesAsync<RolloutUploader.ScorecardEntity>(scorecardsTable);
            scorecardEntries.Sort((x, y) => x.Date.CompareTo(y.Date));
            List<DeploymentController.AnnotationEntity> deploymentEntries = await GetAllTableEntriesAsync<DeploymentController.AnnotationEntity>(deploymentsTable);
            deploymentEntries.Sort((x, y) => (x.Ended ?? DateTimeOffset.MaxValue).CompareTo(y.Ended ?? DateTimeOffset.MaxValue));

            var relevantDeployments = deploymentEntries.Where(d => (d.Ended ?? DateTimeOffset.MaxValue) > scorecardEntries.Last().Date);

            if (relevantDeployments.Count() > 0)
            {
                if ((relevantDeployments.Last().Ended ?? DateTimeOffset.MaxValue) < DateTimeOffset.UtcNow - TimeSpan.FromDays(2))
                {
                    var scorecards = new List<Scorecard>();

                    SecretBundle githubPat;
                    using (KeyVaultClient kv = new KeyVaultClient(new KeyVaultClient.AuthenticationCallback(tokenProvider.KeyVaultTokenCallback)))
                    {
                        githubPat = await kv.GetSecretAsync(RolloutScorer.Utilities.KeyVaultUri, RolloutScorer.Utilities.GitHubPatSecretName);
                    }

                    foreach (var deploymentGroup in relevantDeployments.GroupBy(d => d.Service))
                    {
                        RolloutScorer.RolloutScorer rolloutScorer = new RolloutScorer.RolloutScorer
                        {
                            Repo = deploymentGroup.Key,
                            RolloutStartDate = deploymentGroup.First().Started.GetValueOrDefault().Date,
                            RolloutWeightConfig = Utilities.DefaultConfig.RolloutWeightConfig,
                            GithubConfig = Utilities.DefaultConfig.GithubConfig
                        };
                        rolloutScorer.RepoConfig = Utilities.DefaultConfig.RepoConfigs.Find(r => r.Repo == rolloutScorer.Repo);
                        rolloutScorer.AzdoConfig = Utilities.DefaultConfig.AzdoInstanceConfigs.Find(a => a.Name == rolloutScorer.RepoConfig.AzdoInstance);

                        using (KeyVaultClient kv = new KeyVaultClient(new KeyVaultClient.AuthenticationCallback(tokenProvider.KeyVaultTokenCallback)))
                        {
                            rolloutScorer.SetupHttpClient(await kv.GetSecretAsync(rolloutScorer.AzdoConfig.KeyVaultUri, rolloutScorer.AzdoConfig.PatSecretName));
                        }
                        rolloutScorer.SetupGithubClient(githubPat);

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

                        scorecards.Add(await Scorecard.CreateScorecardAsync(rolloutScorer));
                        log.LogInformation($"Successfully created scorecard for {rolloutScorer.RolloutStartDate.Date} rollout of {rolloutScorer.Repo}.");
                    }

                    log.LogInformation($"Uploading results for {string.Join(", ", scorecards.Select(s => s.Repo))}");
                    await RolloutUploader.UploadResultsAsync(scorecards, RolloutScorer.Utilities.GetGithubClient(githubPat), scorecardsStorageAccountKey, Utilities.DefaultConfig.GithubConfig);
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
