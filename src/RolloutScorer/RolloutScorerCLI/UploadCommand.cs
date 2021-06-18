using Microsoft.Azure.KeyVault;
using Microsoft.Azure.KeyVault.Models;
using Microsoft.Azure.Services.AppAuthentication;
using Microsoft.Extensions.Logging;
using Mono.Options;
using RolloutScorer;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace RolloutScorerCLI
{
    public class UploadCommand : Command
    {
        public UploadCommand() : base("upload", "The upload command takes a series of inline arguments which specify the" +
            "locations of the scorecard CSV files to upload. Each of these files will be combined into a single scorecard document.\n\n" +
            "\"Uploading\" the file here means making a PR to core-eng containing adding the scorecard to '/Documentation/Rollout-Scorecards/'" +
            "and placing the data in Kusto which backs a PowerBI dashboard.\n\n" +
            "usage: RolloutScorer upload [CSV_FILE_1] [CSV_FILE_2] ...\n")
        {

        }

        public override int Invoke(IEnumerable<string> arguments)
        {
            return InvokeAsync(arguments).GetAwaiter().GetResult();
        }
        private async Task<int> InvokeAsync(IEnumerable<string> arguments)
        {
            if (arguments.Count() == 0)
            {
                Utilities.WriteError($"Invalid number of arguments ({arguments.Count()} provided to command 'upload'; must specify at least one CSV to upload");
                return 1;
            }

            // Get the GitHub PAT and storage account connection string from key vault
            AzureServiceTokenProvider tokenProvider = new AzureServiceTokenProvider();
            SecretBundle githubPat;
            SecretBundle storageAccountConnectionString;
            using (KeyVaultClient kv = new KeyVaultClient(new KeyVaultClient.AuthenticationCallback(tokenProvider.KeyVaultTokenCallback)))
            {
                Console.WriteLine("Fetching PAT and connection string from key vault.");
                githubPat = await kv.GetSecretAsync(Utilities.KeyVaultUri, Utilities.GitHubPatSecretName);
                storageAccountConnectionString = await kv.GetSecretAsync(Utilities.KeyVaultUri, ScorecardsStorageAccount.KeySecretName);
            }

            return await RolloutUploader.UploadResultsAsync(arguments.ToList(), storageAccountConnectionString.Value);
        }
    }
}
