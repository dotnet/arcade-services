using Microsoft.Azure.KeyVault;
using Microsoft.Azure.KeyVault.Models;
using Microsoft.Azure.Services.AppAuthentication;
using Microsoft.Extensions.Logging;
using Mono.Options;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace RolloutScorer
{
    public class ScoreCommand : Command
    {
        private RolloutScorer _rolloutScorer = new RolloutScorer();
        private bool _showHelp;

        public ScoreCommand() : base("score", "Scores the specified rollout")
        {
            Options = new OptionSet()
            {
                "The score command calculates a particular rollout's score and generates the scorecard which it outputs as a CSV for review.",
                "Optionally, it can skip this output step and upload the results directly.",
                "",
                "usage: RolloutScorer score [OPTIONS]",
                "",
                { "r|repo=", "The repository to score", r => _rolloutScorer.Repo = r },
                { "b|branch=", "The branch of the repo to score (e.g. servicing or prod); defaults to production", b => _rolloutScorer.Branch = $"refs/heads/{b}" },
                { "s|rollout-start-date=", "The date on which the rollout started", s => _rolloutScorer.RolloutStartDate = DateTimeOffset.Parse(s) },
                { "e|rollout-end-date=", "The date on which the rollout ended; defaults to current date",
                    e => _rolloutScorer.RolloutEndDate = DateTimeOffset.Parse(e).AddHours(23).AddMinutes(59).AddSeconds(59) },
                { "manual-rollbacks=", "The number of rollbacks which weren't deployed by builds (e.g. clicking a button to return to a previous state); defaults to 0", (int r) => _rolloutScorer.ManualRollbacks = r },
                { "manual-hotfixes=", "Any extra hotfixes that won't be tracked by the tool (e.g. database changes)", (int h) => _rolloutScorer.ManualHotfixes = h },
                { "assume-no-tags", "Assumes no '[HOTFIX]' tags and just calculates hotfixes based on number of deployments after the first", notags => _rolloutScorer.AssumeNoTags = notags != null },
                { "d|downtime=", "A TimeSpan specifying an amount of downtime which occurred; defaults to 0", d => _rolloutScorer.Downtime = TimeSpan.Parse(d) },
                { "f|failed", $"Indicates a failed rollout", f => _rolloutScorer.Failed = f != null },
                { "o|output=", "Filename to output the generated csv to; defaults to ./{repo}-scorecard.csv", o => _rolloutScorer.OutputFile = o },
                { "skip-output", "Skips the output step", skip => _rolloutScorer.SkipOutput = skip != null },
                { "upload", "Directly uploads results", upload => _rolloutScorer.Upload = upload != null },
                { "debug", "Enables debug logging", l => _rolloutScorer.LogLevel = LogLevel.Debug },
                { "v|verbose", "Enables verbose (trace) logging", l => _rolloutScorer.LogLevel = LogLevel.Trace },
                { "h|help", "Displays this message and exits", h => _showHelp = h != null },
            };
        }

        public override int Invoke(IEnumerable<string> arguments)
        {
            return InvokeAsync(arguments).GetAwaiter().GetResult();
        }

        public async Task<int> InvokeAsync(IEnumerable<string> arguments)
        {
            Options.Parse(arguments);

            if (_showHelp)
            {
                Options.WriteOptionDescriptions(CommandSet.Out);
                return 0;
            }

            if (string.IsNullOrEmpty(_rolloutScorer.OutputFile))
            {
                _rolloutScorer.OutputFile = Path.Combine(Directory.GetCurrentDirectory(),
                    $"{_rolloutScorer.Repo}-{_rolloutScorer.RolloutStartDate.Date.ToShortDateString().Replace("/","-")}-scorecard.csv");
            }

            Config config = Utilities.ParseConfig();
            if (config == null)
            {
                return 1;
            }
            _rolloutScorer.RolloutWeightConfig = config.RolloutWeightConfig;
            _rolloutScorer.GithubConfig = config.GithubConfig;

            // If they haven't told us to upload but they also haven't specified a repo & rollout start date, we need to throw
            if (string.IsNullOrEmpty(_rolloutScorer.Repo) || (_rolloutScorer.RolloutStartDate == null))
            {
                Utilities.WriteError($"ERROR: One or both of required parameters 'repo' and 'rollout-start-date' were not specified.");
                return 1;
            }

            _rolloutScorer.RepoConfig = config.RepoConfigs.Find(r => r.Repo == _rolloutScorer.Repo);
            if (_rolloutScorer.RepoConfig == null)
            {
                Utilities.WriteError($"ERROR: Provided repo '{_rolloutScorer.Repo}' does not exist in config file");
                return 1;
            }

            _rolloutScorer.AzdoConfig = config.AzdoInstanceConfigs.Find(a => a.Name == _rolloutScorer.RepoConfig.AzdoInstance);
            if (_rolloutScorer.AzdoConfig == null)
            {
                Utilities.WriteError($"ERROR: Configuration file is invalid; repo '{_rolloutScorer.RepoConfig.Repo}' " +
                    $"references unknown AzDO instance '{_rolloutScorer.RepoConfig.AzdoInstance}'");
                return 1;
            }

            // Get the AzDO & GitHub PATs from key vault
            AzureServiceTokenProvider tokenProvider = new AzureServiceTokenProvider();
            SecretBundle githubPat;
            SecretBundle storageAccountConnectionString;
            using (KeyVaultClient kv = new KeyVaultClient(new KeyVaultClient.AuthenticationCallback(tokenProvider.KeyVaultTokenCallback)))
            {
                Console.WriteLine("Fetching PATs from key vault.");
                _rolloutScorer.SetupHttpClient((await kv.GetSecretAsync(_rolloutScorer.AzdoConfig.KeyVaultUri, _rolloutScorer.AzdoConfig.PatSecretName)).Value);
                githubPat = await kv.GetSecretAsync(Utilities.KeyVaultUri, Utilities.GitHubPatSecretName);
                _rolloutScorer.SetupGithubClient(githubPat.Value);
                storageAccountConnectionString = await kv.GetSecretAsync(Utilities.KeyVaultUri, ScorecardsStorageAccount.KeySecretName);
            }

            try
            {
                await _rolloutScorer.InitAsync();
            }
            catch (ArgumentException e)
            {
                Utilities.WriteError(e.Message);
                return 1;
            }

            Scorecard scorecard = await Scorecard.CreateScorecardAsync(_rolloutScorer);
            string expectedTimeToRollout = TimeSpan.FromMinutes(_rolloutScorer.RepoConfig.ExpectedTime).ToString();

            Console.WriteLine($"The {_rolloutScorer.Repo} {_rolloutScorer.RolloutStartDate.Date.ToShortDateString()} rollout score is {scorecard.TotalScore}.\n");
            Console.WriteLine($"|              Metric              |   Value  |  Target  |   Score   |");
            Console.WriteLine($"|:--------------------------------:|:--------:|:--------:|:---------:|");
            Console.WriteLine($"| Time to Rollout                  | {scorecard.TimeToRollout} | {expectedTimeToRollout} |     {scorecard.TimeToRolloutScore}     |");
            Console.WriteLine($"| Critical/blocking issues created |     {scorecard.CriticalIssues}    |    0     |     {scorecard.CriticalIssueScore}     |");
            Console.WriteLine($"| Hotfixes                         |     {scorecard.Hotfixes}    |    0     |     {scorecard.HotfixScore}     |");
            Console.WriteLine($"| Rollbacks                        |     {scorecard.Rollbacks}    |    0     |     {scorecard.RollbackScore}     |");
            Console.WriteLine($"| Service downtime                 | {scorecard.Downtime} | 00:00:00 |     {scorecard.DowntimeScore}     |");
            Console.WriteLine($"| Failed to rollout                |   {scorecard.Failure.ToString().ToUpperInvariant()}  |   FALSE  |     {(scorecard.Failure ? config.RolloutWeightConfig.FailurePoints : 0)}     |");
            Console.WriteLine($"| Total                            |          |          |   **{scorecard.TotalScore}**   |");

            if (_rolloutScorer.Upload)
            {
                Console.WriteLine("Directly uploading results.");
                await RolloutUploader.UploadResultsAsync(new List<Scorecard> { scorecard }, Utilities.GetGithubClient(githubPat.Value), storageAccountConnectionString.Value, _rolloutScorer.GithubConfig);
            }

            if (_rolloutScorer.SkipOutput)
            {
                Console.WriteLine("Skipping output step.");
            }
            else
            {
                if (await scorecard.Output(_rolloutScorer.OutputFile) != 0)
                {
                    return 1;
                }
                Console.WriteLine($"Wrote output to file {_rolloutScorer.OutputFile}");
            }

            return 0;
        }
    }

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

            return await RolloutUploader.UploadResultsAsync(arguments.ToList(), Utilities.ParseConfig(), Utilities.GetGithubClient(githubPat.Value), storageAccountConnectionString.Value);
        }
    }
}
