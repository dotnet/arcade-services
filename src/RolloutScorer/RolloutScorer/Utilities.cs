using Microsoft.Extensions.Logging;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Table;
using Newtonsoft.Json;
using Octokit;
using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;

namespace RolloutScorer
{
    public class Utilities
    {
        public const string KeyVaultUri = "https://engkeyvault.vault.azure.net";
        public const string GitHubPatSecretName = "BotAccount-dotnet-bot-repo-PAT";

        public static bool IssueContainsRelevantLabels(Issue issue, string issueLabel, string repoLabel, ILogger log = null)
        {
            if (issue == null)
            {
                WriteWarning("A null issue was passed.", log);
                return false;
            }

            return issue.Labels.Any(l => l.Name == issueLabel) && issue.Labels.Any(l => l.Name == repoLabel);
        }

        public static Config ParseConfig()
        {
            Config config;
            string configFile = $"{AppContext.BaseDirectory}/config.json";
            if (!File.Exists(configFile))
            {
                WriteError($"ERROR: Config file not found; expected it to be at '{configFile}'");
                return null;
            }
            using (JsonTextReader reader = new JsonTextReader(new StreamReader(configFile)))
            {
                config = JsonSerializer.Create().Deserialize<Config>(reader);
            }
            return config;
        }
        public static string HandleApiRedirect(HttpResponseMessage redirect, Uri apiRequest, ILogger log = null)
        {
            // Since the API will sometimes 302 us, we're going to do a quick check to see
            // that we're still being sent to AzDO and not some random location
            // If so, we'll provide our auth so we don't get 401'd
            Uri redirectUri = redirect.Headers.Location;
            if (redirectUri.Scheme.ToLower() != "https")
            {
                WriteError($"API attempted to redirect to using incorrect scheme (expected 'https', was '{redirectUri.Scheme}'", log);
                WriteError($"Request URI: '{apiRequest}'\nRedirect URI: '{redirectUri}'", log);
                throw new HttpRequestException("Bad redirect scheme");
            }
            else if (redirectUri.Host != apiRequest.Host)
            {
                WriteError($"API attempted to redirect to unknown host '{redirectUri.Host}' (expected '{apiRequest.Host}'); not passing auth parameters", log);
                WriteError($"Request URI: '{apiRequest}'\nRedirect URI: '{redirectUri}'", log);
                throw new HttpRequestException("Bad redirect host");
            }
            else
            {
                return redirectUri.ToString();
            }
        }

        public static CloudTable GetScorecardsCloudTable(string storageAccountKey)
        {
            CloudStorageAccount storageAccount = CloudStorageAccount.Parse(
                connectionString: $"DefaultEndpointsProtocol=https;AccountName={ScorecardsStorageAccount.Name};AccountKey={storageAccountKey};EndpointSuffix=core.windows.net");
            CloudTableClient tableClient = storageAccount.CreateCloudTableClient();
            return tableClient.GetTableReference(ScorecardsStorageAccount.ScorecardsTableName);
        }

        public static GitHubClient GetGithubClient(string githubPat)
        {
            ProductInfoHeaderValue productHeader = Program.GetProductInfoHeaderValue();
            GitHubClient githubClient = new GitHubClient(new Octokit.ProductHeaderValue(productHeader.Product.Name, productHeader.Product.Version))
            {
                Credentials = new Credentials("fake", githubPat)
            };

            return githubClient;
        }

        public static void WriteError(string message, ILogger log = null)
        {
            if (log == null)
            {
                WriteColoredMessage(message, ConsoleColor.Red);
            }
            else
            {
                log.LogError(message);
            }
        }

        public static void WriteWarning(string message, ILogger log)
        {
            if (log == null)
            {
                WriteColoredMessage(message, ConsoleColor.Yellow);
            }
            else
            {
                log.LogWarning(message);
            }
        }

        private static void WriteColoredMessage(string message, ConsoleColor textColor)
        {
            ConsoleColor currentTextColor = Console.ForegroundColor;
            Console.ForegroundColor = textColor;
            Console.WriteLine(message);
            Console.ForegroundColor = currentTextColor;
        }
    }

    public static class AzureDevOpsCommitTags
    {
        public const string HotfixTag = "[HOTFIX]";
        public const string RollbackTag = "[ROLLBACK]";
    }

    public static class GithubLabelNames
    {
        public const string IssueLabel = "Rollout Issue";
        public const string HotfixLabel = "Rollout Hotfix";
        public const string RollbackLabel = "Rollout Rollback";
        public const string DowntimeLabel = "Rollout Downtime";
    }

    public static class ScorecardsStorageAccount
    {
        public const string KeySecretName = "rolloutscorecards-storage-key";
        public const string Name = "rolloutscorecards";
        public const string ScorecardsTableName = "scorecards";
    }
}
