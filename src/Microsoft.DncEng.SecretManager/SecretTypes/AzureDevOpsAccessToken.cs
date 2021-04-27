using System;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.DncEng.CommandLineLib;

namespace Microsoft.DncEng.SecretManager.SecretTypes
{
    [Name("azure-devops-access-token")]
    public class AzureDevOpsAccessToken : SecretType<AzureDevOpsAccessToken.Parameters>
    {
        private readonly ISystemClock _clock;
        private readonly IConsole _console;
        private readonly TimeSpan _rotateBeforeExpiration = new TimeSpan(-15, 0, 0, 0);
        private readonly Regex _patExpirationRegex = new Regex(@"^\s+\d{1,2}/\d{1,2}/\d{4}\s+$");

        public class Parameters
        {
            public string Name { get; set; }
            public string Organization { get; set; }
            public string GitHubBotAccountSecret { get; set; }
            public string GitHubBotAccountName { get; set; }
        }

        public AzureDevOpsAccessToken(ISystemClock clock, IConsole console)
        {
            _clock = clock;
            _console = console;
        }

        protected override async Task<SecretData> RotateValue(Parameters parameters, RotationContext context, CancellationToken cancellationToken)
        {
            if (!_console.IsInteractive)
            {
                throw new InvalidOperationException($"User intervention required for creation or rotation of GitHub bot account.");
            }

            await GitHubBotAccount.ShowLoginInformation(context, _console, _clock, parameters.GitHubBotAccountSecret, parameters.GitHubBotAccountName);

            var expiration = await _console.AskUser("PAT expiration (M/d/yyyy)",
                "PAT expiration format must be M/d/yyyy.",
                l => DateTime.TryParseExact(l, "M/d/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out _),
                l => DateTime.ParseExact(l, "M/d/yyyy", CultureInfo.InvariantCulture));

            var pat = await _console.AskUser("PAT",
                "PAT must have at least 52 characters.",
                l => l != null && l.Length >= 52);

            return new SecretData(pat, expiration, expiration.Add(_rotateBeforeExpiration));
        }
    }
}
