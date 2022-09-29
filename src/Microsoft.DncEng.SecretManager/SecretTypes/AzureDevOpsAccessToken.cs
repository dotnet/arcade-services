using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.DncEng.CommandLineLib;

namespace Microsoft.DncEng.SecretManager.SecretTypes
{
    [Name("azure-devops-access-token")]
    public class AzureDevOpsAccessToken : SecretType<AzureDevOpsAccessToken.Parameters>
    {
        private readonly TimeSpan _rotateBeforeExpiration = TimeSpan.FromDays(-15);

        public class Parameters
        {
            public string Organizations { get; set; }
            public SecretReference DomainAccountSecret { get; set; }
            public string DomainAccountName { get; set; }
            public string Scopes { get; set; }
        }

        public ISystemClock Clock { get; }
        public IConsole Console { get; }

        public AzureDevOpsAccessToken(ISystemClock clock, IConsole console)
        {
            Clock = clock;
            Console = console;
        }

        protected override async Task<SecretData> RotateValue(Parameters parameters, RotationContext context, CancellationToken cancellationToken)
        {
            if (!Console.IsInteractive)
            {
                throw new HumanInterventionRequiredException($"User intervention required for creation or rotation of an Azure DevOps access token.");
            }

            if (string.IsNullOrEmpty(parameters.Organizations))
            {
                throw new ArgumentException("Organizations is required.");
            }

            if (string.IsNullOrEmpty(parameters.Scopes))
            {
                throw new ArgumentException("Scopes is required.");
            }

            string patGeneratorParams = $"--scopes {parameters.Scopes} --organizations {parameters.Organizations} --name {context.SecretName} --expires-in 180";

            var password = await context.GetSecretValue(parameters.DomainAccountSecret);
            Console.WriteLine($"Please run `dotnet pat-generator {patGeneratorParams}`\nLog in using account {parameters.DomainAccountName} and password: {password}");

            var expiration = await Console.PromptAndValidateAsync("PAT expiration (M/d/yyyy)",
                "PAT expiration format must be M/d/yyyy.",
                (string value, out DateTime parsedValue) => DateTime.TryParseExact(value, "M/d/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out parsedValue));

            var pat = await Console.PromptAndValidateAsync("PAT",
                "PAT must have at least 52 characters.",
                value => value != null && value.Length >= 52);

            return new SecretData(pat, expiration, expiration.Add(_rotateBeforeExpiration));
        }
    }
}
