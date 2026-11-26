// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text;
using System.Text.Json;
using Azure.Identity;
using Azure.Security.KeyVault.Keys.Cryptography;

#nullable enable
namespace ProductConstructionService.ScenarioTests.Helpers;

/// <summary>
/// Mints a GitHub App installation token the same way the E2E pipeline does (see eng/Get-GitHubAppToken.ps1).
/// The App's private key is a non-exportable Key Vault key, so the JWT is signed in Key Vault using the
/// Azure CLI login, and then exchanged for an installation token via Octokit.
/// </summary>
internal static class GitHubAppInstallationTokenProvider
{
    private const string KeyVaultKeyUri = "https://ProductConstructionInt.vault.azure.net/keys/maestro-ci-private-key";
    private const string AppClientId = "Iv23lirMWEG8ISMvxF43";
    private const string ProductName = "arcade-services-scenario-tests";

    public static async Task<string> GetInstallationTokenAsync(string installationOwner)
    {
        string jwt = await CreateAppJwtAsync();

        var appClient = new Octokit.GitHubClient(new Octokit.ProductHeaderValue(ProductName))
        {
            Credentials = new Octokit.Credentials(jwt, Octokit.AuthenticationType.Bearer),
        };

        Octokit.Installation installation = await appClient.GitHubApps.GetOrganizationInstallationForCurrent(installationOwner);
        Octokit.AccessToken accessToken = await appClient.GitHubApps.CreateInstallationToken(installation.Id);
        return accessToken.Token;
    }

    private static async Task<string> CreateAppJwtAsync()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        string header = Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(new { alg = "RS256", typ = "JWT" }));
        string payload = Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(new
        {
            iat = now.AddMinutes(-1).ToUnixTimeSeconds(),
            exp = now.AddMinutes(5).ToUnixTimeSeconds(),
            iss = AppClientId,
        }));
        string signingInput = $"{header}.{payload}";

        var cryptographyClient = new CryptographyClient(new Uri(KeyVaultKeyUri), new AzureCliCredential());
        SignResult signResult = await cryptographyClient.SignDataAsync(SignatureAlgorithm.RS256, Encoding.UTF8.GetBytes(signingInput));

        return $"{signingInput}.{Base64UrlEncode(signResult.Signature)}";
    }

    private static string Base64UrlEncode(byte[] bytes)
        => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
