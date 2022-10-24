// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.DelegatedAuthorization;
using Microsoft.VisualStudio.Services.WebApi;
using System;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Text.Json;
using System.Collections.Generic;
using Microsoft.VisualStudio.Services.DelegatedAuthorization.WebApi;

namespace Microsoft.DncEng.PatGenerator;

class AzureDevOpsOrganizationModel
{
    public string AccountName { get; set; }
    public Guid AccountId { get; set; }
    public string AccountUri { get; set; }
}

public class AzureDevOpsPATGenerator
{
    private const string OAuth2Endpoint = "https://app.vssps.visualstudio.com";
    private readonly VssCredentials credentials;
    public AzureDevOpsPATGenerator(VssCredentials credentials)
    {
        this.credentials = credentials;
    }

    /// <summary>
    ///     Generate an azure devops PAT with a given name, target organization set, and scopes.
    /// </summary>
    /// <param name="name"></param>
    /// <param name="targetScopes"></param>
    /// <param name="targetOrganizationNames"></param>
    /// <param name="validTo"></param>
    /// <returns>New PAT</returns>
    /// <exception cref="ArgumentException"></exception>
    /// <exception cref="Exception"></exception>
    public async Task<SessionToken> GeneratePATAsync(
        string name,
        AzureDevOpsPATScopes targetScopes,
        IEnumerable<string> targetOrganizationNames,
        DateTime validTo)
    {
        ValidateParameters(name, targetScopes, targetOrganizationNames, validTo);

        var minimalScopesList = targetScopes.GetMinimizedScopeList();
        var scopesWithPrefixes = minimalScopesList.Select(scope => $"vso.{scope}");
        string scopes = string.Join(" ", scopesWithPrefixes);

        try
        {
            using var tokenConnection = new VssConnection(
                new Uri("https://vssps.dev.azure.com/"),
                credentials);
            using var tokenClient = await tokenConnection.GetClientAsync<TokenHttpClient>();

            var organizations = await GetAccountsByNameAsync(targetOrganizationNames);

            var tokenInfo = new SessionToken()
            {
                DisplayName = name,
                Scope = scopes,
                TargetAccounts = organizations.Select(account => account.AccountId).ToArray(),
                ValidFrom = DateTime.UtcNow,
                ValidTo = validTo,
            };

            SessionToken pat = await tokenClient.CreateSessionTokenAsync(
                tokenInfo,
                SessionTokenType.Compact,
                isPublic: false);
            return pat;
        }
        catch (Exception ex)
        {
            throw new Exception($"Failed to generate a PAT named '{name}' for organizatons '{string.Join(", ", targetOrganizationNames)}' and scopes '{scopes}'", ex);
        }
    }

    private static void ValidateParameters(string name, AzureDevOpsPATScopes targetScopes, IEnumerable<string> targetOrganizationNames, DateTime validTo)
    {
        if (string.IsNullOrEmpty(name))
        {
            throw new ArgumentException("PAT must have a name");
        }

        if (targetScopes == 0)
        {
            throw new ArgumentException("PAT must have one or more desired scopes.");
        }

        if (!targetOrganizationNames.Any())
        {
            throw new ArgumentException("PAT must have one or more orgnanizations.");
        }

        if (validTo.CompareTo(DateTime.Now) < 0)
        {
            throw new ArgumentException("PAT expiration date must be after the current date.");
        }
    }

    /// <summary>
    /// Get more detailed information on the desired organiztions.
    /// </summary>
    /// <param name="organizationNames">Organizations to look up.</param>
    /// <returns>Detailed information on the organizations.</returns>
    private async Task<List<AzureDevOpsOrganizationModel>> GetAccountsByNameAsync(IEnumerable<string> organizationNames)
    {
        List<AzureDevOpsOrganizationModel> availableOrganizations = null;

        try
        {
            using var connection = new VssConnection(new Uri(OAuth2Endpoint), credentials);
            using var client = new HttpClient(connection.InnerHandler); // lgtm [cs/httpclient-checkcertrevlist-disabled] dependant on client library behavior
            using var response = await client.GetAsync($"{OAuth2Endpoint}/_apis/accounts");
            availableOrganizations = await JsonSerializer.DeserializeAsync<List<AzureDevOpsOrganizationModel>>(await response.Content.ReadAsStreamAsync());
        }
        catch (Exception ex)
        {
            throw new Exception("Failed to obtain list of available organizations", ex);
        }

        // Walk each one we want and find a matching organization in the returned info.
        List<AzureDevOpsOrganizationModel> matchingOrganizations = new List<AzureDevOpsOrganizationModel>();
        foreach (var desiredOrganization in organizationNames)
        {
            var matchingOrganization = availableOrganizations.SingleOrDefault(org => 
                org.AccountName.Equals(desiredOrganization, StringComparison.OrdinalIgnoreCase));
            if (matchingOrganization != null)
            {
                matchingOrganizations.Add(matchingOrganization);
            }
            else
            {
                throw new Exception($"Could not location any information on organization '{desiredOrganization}'.");
            }
        }
            
        return matchingOrganizations;
    }
}
