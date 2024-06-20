// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Azure.Core;
using Azure.Identity;
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

    private readonly Dictionary<string, ManagedIdentityCredential> _tokenCredentials = [];
    private readonly IOptionsMonitor<AzureDevOpsTokenProviderOptions> _options;

    public AzureDevOpsTokenProvider(IOptionsMonitor<AzureDevOpsTokenProviderOptions> options)
    {
        _options = options;

        foreach (var credential in options.CurrentValue.ManagedIdentities)
        {
            _tokenCredentials[credential.Key] = credential.Value == "system"
                ? new ManagedIdentityCredential()
                : new ManagedIdentityCredential(credential.Value);
        }
    }

    public async Task<string> GetTokenForAccount(string account)
    {
        if (_options.CurrentValue.Tokens.TryGetValue(account, out var pat) && !string.IsNullOrEmpty(pat))
        {
            return pat;
        }

        if (_tokenCredentials.TryGetValue(account, out var credential))
        {
            return (await credential.GetTokenAsync(new TokenRequestContext([AzureDevOpsScope]))).Token;
        }

        // We can also define just one MI for all accounts
        if (_tokenCredentials.TryGetValue("default", out var defaultCredential))
        {
            return (await defaultCredential.GetTokenAsync(new TokenRequestContext([AzureDevOpsScope]))).Token;
        }

        throw new ArgumentOutOfRangeException(
            $"Azure DevOps account {account} does not have a configured PAT or credential. " +
            $"Please add the account to the 'AzureDevOps.Tokens' or 'AzureDevOps.ManagedIdentities' configuration section");
    }
}
