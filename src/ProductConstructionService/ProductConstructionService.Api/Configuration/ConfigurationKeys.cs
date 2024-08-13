// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace ProductConstructionService.Api.Configuration;

internal static class ConfigurationKeys
{
    public const string DatabaseConnectionString = "BuildAssetRegistrySqlConnectionString";
    public const string ManagedIdentityId = "ManagedIdentityClientId";
    public const string EntraAuthenticationKey = "EntraAuthentication";
    public const string KeyVaultName = "KeyVaultName";
    public const string GitHubConfiguration = "GitHub";
    public const string AzureDevOpsConfiguration = "AzureDevOps";
    public const string GitHubToken = "BotAccount-dotnet-bot-repo-PAT";
    public const string GitHubClientId = "github-oauth-id";
    public const string GitHubClientSecret = "github-oauth-secret";
    public const string MaestroUri = "Maestro:Uri";
    public const string MaestroNoAuth = "Maestro:NoAuth";
    public const string DependencyFlowSLAs = "DependencyFlowSLAs";
    public const string DataProtection = "DataProtection";
    public const string DataProtectionKeyBlobUri = DataProtection + ":KeyBlobUri";
    public const string DataProtectionKeyUri = DataProtection + ":DataProtectionKeyUri";
}
