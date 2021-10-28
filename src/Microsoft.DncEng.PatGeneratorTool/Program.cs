using Azure.Core;
using Azure.Identity;
using Microsoft.DncEng.PatGenerator;
using Microsoft.IdentityModel.Clients.ActiveDirectory;
using Microsoft.VisualStudio.Services.Client;
using Microsoft.VisualStudio.Services.Common;
using System;
using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.Threading.Tasks;

namespace Microsoft.DncEng.PatGeneratorTool
{
    class Program
    {
        private const int DefaultExpirationInDays = 30;

        static int Main(string[] args)
        {
            var possibleEnumScopes = string.Join(", ", Enum.GetNames(typeof(AzureDevOpsPATScopes)));
            var cmd = new RootCommand
            {
                new Option<List<AzureDevOpsPATScopes>>("--scopes",
                    parseArgument: arg => ParsePATScopes(arg),
                    description: $"PAT scopes. Valid values are: {possibleEnumScopes}.")
                {
                    IsRequired = true,
                    Arity = ArgumentArity.OneOrMore,
                    AllowMultipleArgumentsPerToken = true,
                },
                new Option<string[]>("--organizations", "Organizations that the PAT should apply to.")
                {
                    IsRequired = true,
                    Arity = ArgumentArity.OneOrMore,
                    AllowMultipleArgumentsPerToken = true,
                },
                new Option<string>("--name", "Optional name of the PAT. If no name is specified, then uses a standard naming format of orgs-scopes"),
                new Option<int?>("--expires-in", @"Number of days that the PAT should expire in. Cannot be used in conjunction with --expiration"),
                new Option<DateTime?>("--expiration", @"Date that the PAT should expire on. Cannot be used in conjunction with --expires-in"),
                new Option<string>("--user", @"DOMAIN\username of a specific domain user to whom the PAT should belong. Only works while **physically** connected to corp."),
                new Option<string>("--password", @"password of a specific domain user to whom the PAT should belong. Only works while **physically** connected to corp."),
            };

            cmd.Handler = CommandHandler.Create<List<AzureDevOpsPATScopes>, string[], string, int?, DateTime?, string, string, IConsole>(Go);

            return cmd.Invoke(args);
        }

        /// <summary>
        /// Parse out the scope strings into PAT enumeration values 
        /// </summary>
        /// <param name="argumentResult"></param>
        /// <returns></returns>
        private static List<AzureDevOpsPATScopes> ParsePATScopes(System.CommandLine.Parsing.ArgumentResult argumentResult)
        {
            List<AzureDevOpsPATScopes> scopes = new List<AzureDevOpsPATScopes>();
            foreach (var token in argumentResult.Tokens)
            {
                if (Enum.TryParse(token.Value, ignoreCase: true, out AzureDevOpsPATScopes patScope))
                {
                    scopes.Add(patScope);
                }
                else
                {
                    argumentResult.ErrorMessage = $"'{token.Value}' is not a valid scope";
                }
            }

            return scopes;
        }

        private static async Task<int> Go(List<AzureDevOpsPATScopes> scopes, string[] organizations, string name, int? expiresIn, DateTime? expiration, string user, string password, IConsole console)
        {
            AzureDevOpsPATScopes scopeFlags = 0;
            foreach (var scope in scopes)
            {
                scopeFlags |= scope;
            }

            string patName = GetPatName(scopeFlags, organizations, name);

            if (expiresIn.HasValue && expiration.HasValue)
            {
                Console.WriteLine("May not specify both --expires-in and --expiration.");
                return 1;
            }

            if (string.IsNullOrEmpty(user) != string.IsNullOrEmpty(password))
            {
                Console.WriteLine("Must specify both user + password, or neither.");
                return 1;
            }

            DateTime credentialExpiration = GetExpirationDate(expiration, expiresIn);

            VssCredentials credentials;
            if (!string.IsNullOrEmpty(user))
            {
                credentials = new VssAadCredential(user, password);
            }
            else
            {
                credentials = await GetInteractiveUserCredentials();
            }

            var patGenerator = new AzureDevOpsPATGenerator(credentials);
            var pat = await patGenerator.GeneratePATAsync(patName, scopeFlags, organizations, credentialExpiration);
            Console.WriteLine($"{patName} (Valid Until: {credentialExpiration}): {pat.Token}");
            return 0;
        }

        /// <summary>
        /// Determine the desired name of the PAT. It appears that PATs may have really any combination of characters
        /// and they do not have to be unique.
        /// </summary>
        /// <param name="scopes">Desired scopes</param>
        /// <param name="organizations">Organizations</param>
        /// <param name="name">Name provided by the user</param>
        /// <returns>Name of the PAT.</returns>
        private static string GetPatName(AzureDevOpsPATScopes scopes, string[] organizations, string name)
        {
            string patName = name;
            if (string.IsNullOrEmpty(patName))
            {
                string scopeString = scopes.GetScopeString();
                patName = $"{string.Join("-", organizations)}-{scopeString}";
            }

            return patName;
        }

        /// <summary>
        /// Compute the desired expiration date. If no expiration day or expiresIn is specified, 
        /// then defaults to 30 days.
        /// </summary>
        /// <param name="expiration">Explicit expiration date</param>
        /// <param name="expiresIn">Explicit number of days to expire in.</param>
        /// <returns>Date that the credential should expire on.</returns>
        private static DateTime GetExpirationDate(DateTime? expiration, int? expiresIn)
        {
            DateTime finalExpiration = DateTime.Now.AddDays(DefaultExpirationInDays);
            if (!expiresIn.HasValue && expiration.HasValue)
            {
                finalExpiration = expiration.Value;
            }
            else if (expiresIn.HasValue && !expiration.HasValue)
            {
                finalExpiration = DateTime.Now.AddDays(expiresIn.Value);
            }

            return finalExpiration;
        }

        readonly static string[] AzureDevOpsAuthScopes = new string[] { "499b84ac-1321-427f-aa17-267ca6975798/user_impersonation" };

        /// <summary>
        /// Get interactive user credentials for Azure Devops. 
        /// </summary>
        /// <returns>AzDO Credentials</returns>
        private static async Task<VssCredentials> GetInteractiveUserCredentials()
        {
            var browserCredential = new InteractiveBrowserCredential();
            var context = new TokenRequestContext(AzureDevOpsAuthScopes);
            var authToken = await browserCredential.GetTokenAsync(context, System.Threading.CancellationToken.None);
            return new VssCredentials(new VssBasicCredential("", authToken.Token));
        }
    }
}
