// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text.RegularExpressions;
using Azure.Core;
using Azure.Identity;
using Maestro.Common.AppCredentials;
using Microsoft.Extensions.Options;

namespace Maestro.Common.AzureDevOpsTokens;

/// <summary>
/// This token provider expects to have a token or an MI defined per each Azure DevOps account (dnceng, devdiv..).
/// Token has precedence over MI.
/// Example configuration:
/// 
/// {
///   "Tokens": {
///     "dnceng": "[some PAT]",
///   },
///   "ManagedIdentities": {
///     "devdiv": "123b84ac-1321-425f-a117-222ca6974498",
///     "default": "system", // Use the system-assigned identity for every other org not defined
/// }
///
/// The config above will use PAT for dnceng, a specific MI for devdiv and the system-assigned MI for any other org.
/// </summary>
public class AzureDevOpsTokenProvider : IAzureDevOpsTokenProvider
{
    private const string AzureDevOpsScope = "499b84ac-1321-427f-aa17-267ca6975798/.default";
    private static readonly Regex AccountNameRegex = new(@"^https://dev\.azure\.com/(?<account>[a-zA-Z0-9]+)/");

    private readonly Dictionary<string, TokenCredential> _accountCredentials;

    public AzureDevOpsTokenProvider(IOptionsMonitor<AzureDevOpsTokenProviderOptions> options)
        // We don't mind locking down the current value of the option because non-token settings are not expected to change
        // Tokens are always read fresh through the second argument
        : this(GetCredentials(options.CurrentValue, (account, _, _) => options.CurrentValue[account].Token!))
    {
    }

    public static AzureDevOpsTokenProvider FromStaticOptions(AzureDevOpsTokenProviderOptions options)
        => new(GetCredentials(options, (account, _, _) => options[account].Token!));

    private AzureDevOpsTokenProvider(Dictionary<string, TokenCredential> accountCredentials)
    {
        _accountCredentials = accountCredentials;
    }

    public string GetTokenForAccount(string account)
    {
        var credential = GetCredential(account);
        return credential.GetToken(new TokenRequestContext([AzureDevOpsScope]), cancellationToken: default).Token;
    }

    public async Task<string> GetTokenForAccountAsync(string account)
    {
        var credential = GetCredential(account);
        return (await credential.GetTokenAsync(new TokenRequestContext([AzureDevOpsScope]), cancellationToken: default)).Token;
    }

    public string GetTokenForRepository(string repositoryUrl)
    {
        Match m = AccountNameRegex.Match(repositoryUrl);
        if (!m.Success)
        {
            throw new ArgumentException($"{repositoryUrl} is not a valid Azure DevOps repository URL");
        }

        var account = m.Groups["account"].Value;
        return GetTokenForAccount(account);
    }

    public async Task<string?> GetTokenForRepositoryAsync(string repositoryUrl)
    {
        Match m = AccountNameRegex.Match(repositoryUrl);
        if (!m.Success)
        {
            throw new ArgumentException($"{repositoryUrl} is not a valid Azure DevOps repository URL");
        }

        var account = m.Groups["account"].Value;
        return await GetTokenForAccountAsync(account);
    }

    private TokenCredential GetCredential(string account)
    {
        if (_accountCredentials.TryGetValue(account, out var credential))
        {
            return credential;
        }

        if (_accountCredentials.TryGetValue("default", out var defaultCredential))
        {
            return defaultCredential;
        }

        throw new ArgumentOutOfRangeException(
            $"Azure DevOps account {account} does not have a configured PAT or credential. " +
            $"Please add the account to the 'AzureDevOps.Tokens' or 'AzureDevOps.ManagedIdentities' configuration section");
    }

    private static Dictionary<string, TokenCredential> GetCredentials(
        AzureDevOpsTokenProviderOptions options,
        Func<string, TokenRequestContext, CancellationToken, string> patResolver)
    {
        Dictionary<string, TokenCredential> credentials = [];

        foreach (var pair in options)
        {
            var account = pair.Key;
            var option = pair.Value;

            // 0. AzDO PAT from configuration
            if (!string.IsNullOrEmpty(option.Token))
            {
                credentials[account] = new ResolvingCredential((context, cancellationToken) => patResolver(account, context, cancellationToken));
                continue;
            }

            // 1. Managed identity (for server-to-AzDO scenarios)
            if (!string.IsNullOrEmpty(option.ManagedIdentityId))
            {
                credentials[account] = option.ManagedIdentityId == "system"
                    ? new ManagedIdentityCredential()
                    : new ManagedIdentityCredential(option.ManagedIdentityId);
                continue;
            }

            // 2. Azure CLI authentication setup by the caller (for CI scenarios)
            if (option.DisableInteractiveAuth)
            {
                credentials[account] = AppCredential.CreateNonUserCredential(option.AppId);
                continue;
            }

            // 3. Interactive login (user-based scenario)
            credentials[account] = new DefaultAzureCredential(includeInteractiveCredentials: true);
        }

        return credentials;
    }
}
