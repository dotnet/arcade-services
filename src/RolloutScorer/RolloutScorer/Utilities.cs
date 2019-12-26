using Microsoft.Azure.KeyVault.Models;
using Newtonsoft.Json;
using Octokit;
using System;
using System.IO;
using System.Linq;
using System.Net.Http.Headers;
using System.Reflection;

namespace RolloutScorer
{
    public class Utilities
    {
        public const string HotfixAzureDevOpsTag = "[HOTFIX]";
        public const string RollbackAzureDevOpsTag = "[ROLLBACK]";
        public const string IssueLabel = "Rollout Issue";
        public const string HotfixLabel = "Rollout Hotfix";
        public const string RollbackLabel = "Rollout Rollback";
        public const string DowntimeLabel = "Rollout Downtime";

        // The git file mode of a "file blob"; more info here: https://developer.github.com/v3/git/trees/#parameters
        public const string GitFileMode = "100644";

        public const string KeyVaultUri = "https://engkeyvault.vault.azure.net";
        public const string GitHubPatSecretName = "BotAccount-dotnet-bot-repo-PAT";
        public const string StorageAccountKeySecretName = "rolloutscorecards-storage-key";
        public const string StorageAccountName = "rolloutscorecards";
        public const string ScorecardsTableName = "scorecards";

        public static bool IssueContainsRelevantLabels(Issue issue, string issueLabel, string repoLabel)
        {
            if (issue == null)
            {
                WriteWarning("A null issue was passed.");
                return false;
            }

            return issue.Labels.Any(l => l.Name == issueLabel) && issue.Labels.Any(l => l.Name == repoLabel);
        }

        /// <summary>
        /// Parse and return config
        /// </summary>
        /// <returns>Config object representing config</returns>
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

        public static GitHubClient GetGithubClient(SecretBundle githubPat)
        {
            ProductInfoHeaderValue productHeader = GetProductInfoHeaderValue();
            GitHubClient githubClient = new GitHubClient(new Octokit.ProductHeaderValue(productHeader.Product.Name, productHeader.Product.Version))
            {
                Credentials = new Credentials("fake", githubPat.Value)
            };

            return githubClient;
        }

        public static ProductInfoHeaderValue GetProductInfoHeaderValue()
        {
            return new ProductInfoHeaderValue(typeof(Program).Assembly.GetName().Name,
                typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>().InformationalVersion);
        }

        public static void WriteError(string message)
        {
            WriteColoredMessage(message, ConsoleColor.Red);
        }
        public static void WriteWarning(string message)
        {
            WriteColoredMessage(message, ConsoleColor.Yellow);
        }
        private static void WriteColoredMessage(string message, ConsoleColor textColor)
        {
            ConsoleColor currentTextColor = Console.ForegroundColor;
            Console.ForegroundColor = textColor;
            Console.WriteLine(message);
            Console.ForegroundColor = currentTextColor;
        }
    }
}
