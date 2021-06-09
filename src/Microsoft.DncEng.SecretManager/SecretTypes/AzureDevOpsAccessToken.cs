using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.DncEng.CommandLineLib;

namespace Microsoft.DncEng.SecretManager.SecretTypes
{
    [Name("azure-devops-access-token")]
    public class AzureDevOpsAccessToken : GitHubAccountInteractiveSecretType<AzureDevOpsAccessToken.Parameters>
    {
        private readonly TimeSpan _rotateBeforeExpiration = TimeSpan.FromDays(-15);

        public class Parameters
        {
            public string Name { get; set; }
            public string Organization { get; set; }
            public SecretReference GitHubBotAccountSecret { get; set; }
            public SecretReference DomainAccountSecret { get; set; }
            public string GitHubBotAccountName { get; set; }
            public string DomainAccountName { get; set; }
        }

        public AzureDevOpsAccessToken(ISystemClock clock, IConsole console) : base(clock, console)
        {
        }

        protected override async Task<SecretData> RotateValue(Parameters parameters, RotationContext context, CancellationToken cancellationToken)
        {
            if (!Console.IsInteractive)
            {
                throw new InvalidOperationException($"User intervention required for creation or rotation of an Azure DevOps access token.");
            }

            string helpUrl = $"https://dev.azure.com/{parameters.Organization}/_usersSettings/tokens";

            if (parameters.GitHubBotAccountSecret != null)
                await ShowGitHubLoginInformation(context, parameters.GitHubBotAccountSecret, helpUrl, parameters.GitHubBotAccountName);

            if (parameters.DomainAccountSecret != null)
                await ShowDomainAccountInformation(helpUrl, context, parameters.DomainAccountSecret, parameters.DomainAccountName);

            var expiration = await Console.PromptAndValidateAsync("PAT expiration (M/d/yyyy)",
                "PAT expiration format must be M/d/yyyy.",
                (string value, out DateTime parsedValue) => DateTime.TryParseExact(value, "M/d/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out parsedValue));

            var pat = await Console.PromptAndValidateAsync("PAT",
                "PAT must have at least 52 characters.",
                value => value != null && value.Length >= 52);

            return new SecretData(pat, expiration, expiration.Add(_rotateBeforeExpiration));
        }

        protected async Task ShowDomainAccountInformation(string helpUrl, RotationContext context, SecretReference domainAccountSecret, string domainAccountName)
        {
            var passwordReference = new SecretReference { Name = domainAccountSecret.Name, Location = domainAccountSecret.Location };
            var password = await context.GetSecretValue(passwordReference);

            Console.WriteLine($"Please login to {helpUrl} using account {domainAccountName} and password: {password}");
        }
    }
}
