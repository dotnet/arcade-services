using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Azure;
using Azure.Security.KeyVault.Keys;
using Azure.Security.KeyVault.Secrets;
using JetBrains.Annotations;
using Microsoft.DncEng.CommandLineLib;
using Microsoft.DncEng.CommandLineLib.Authentication;

namespace Microsoft.DncEng.SecretManager.StorageTypes;

public class AzureKeyVaultParameters
{
    public Guid Subscription { get; set; }
    public string Name { get; set; }
}
    
[Name("azure-key-vault")]
public class AzureKeyVault : StorageLocationType<AzureKeyVaultParameters>
{
    public static readonly string NextRotationOnTag = "next-rotation-on";
    private readonly TokenCredentialProvider _tokenCredentialProvider;
    private readonly IConsole _console;

    public AzureKeyVault(TokenCredentialProvider tokenCredentialProvider, IConsole console)
    {
        _tokenCredentialProvider = tokenCredentialProvider;
        _console = console;
    }

    private async Task<SecretClient> CreateSecretClient(AzureKeyVaultParameters parameters)
    {
        var creds = await _tokenCredentialProvider.GetCredentialAsync();
        return new SecretClient(new Uri($"https://{parameters.Name}.vault.azure.net/"), creds);
    }

    private async Task<KeyClient> CreateKeyClient(AzureKeyVaultParameters parameters)
    {
        var creds = await _tokenCredentialProvider.GetCredentialAsync();
        return new KeyClient(new Uri($"https://{parameters.Name}.vault.azure.net/"), creds);
    }

    public string GetAzureKeyVaultUri(AzureKeyVaultParameters parameters)
    {
        return $"https://{parameters.Name}.vault.azure.net/";
    }

    public override async Task<List<SecretPropertiesWrapper>> ListSecretsAsync(AzureKeyVaultParameters parameters)
    {
        SecretClient client = await CreateSecretClient(parameters);
        var secrets = new List<SecretPropertiesWrapper>();
        await foreach (var secret in client.GetPropertiesOfSecretsAsync())
        {
            var nextRotationOn = GetNextRotationOn(secret.Name, secret.Tags);
            ImmutableDictionary<string, string> tags = GetTags(secret);
            secrets.Add(new SecretPropertiesWrapper(
                new SecretProperties(secret.Name, secret.ExpiresOn ?? DateTimeOffset.MaxValue, nextRotationOn.offset, tags),
                nextRotationOn.nextRotationFound));
        }

        return secrets;
    }

    private (DateTimeOffset offset, bool nextRotationFound) GetNextRotationOn(string name, IDictionary<string, string> tags)
    {
        bool nextRotationFound = true;
        if (!tags.TryGetValue(NextRotationOnTag, out var nextRotationOnString) ||
            !DateTimeOffset.TryParse(nextRotationOnString, out var nextRotationOn))
        {
            nextRotationOn = DateTimeOffset.MaxValue;
            nextRotationFound = false;
        }

        return (nextRotationOn, nextRotationFound);
    }

    [ItemCanBeNull]
    public override async Task<SecretValue> GetSecretValueAsync(AzureKeyVaultParameters parameters, string name)
    {
        try
        {
            SecretClient client = await CreateSecretClient(parameters);
            Response<KeyVaultSecret> res = await client.GetSecretAsync(name);
            KeyVaultSecret secret = res.Value;
            DateTimeOffset nextRotationOn = GetNextRotationOn(name, secret.Properties.Tags).offset;
            ImmutableDictionary<string, string> tags = GetTags(secret.Properties);
            return new SecretValue(secret.Value, tags, nextRotationOn,
                secret.Properties.ExpiresOn ?? DateTimeOffset.MaxValue);
        }
        catch (RequestFailedException e) when (e.Status == 404)
        {
            return null;
        }
    }

    private static ImmutableDictionary<string, string> GetTags(global::Azure.Security.KeyVault.Secrets.SecretProperties properties)
    {
        ImmutableDictionary<string, string> tags = properties.Tags
            .Where(p => p.Key != NextRotationOnTag)
            .Where(p => p.Key != "Md5")
            .ToImmutableDictionary();
        return tags;
    }

    public override async Task SetSecretValueAsync(AzureKeyVaultParameters parameters, string name, SecretValue value)
    {
        SecretClient client = await CreateSecretClient(parameters);
        var createdSecret = await client.SetSecretAsync(name, value.Value ?? "");
        var properties = createdSecret.Value.Properties;
        foreach (var (k, v) in value.Tags)
        {
            properties.Tags[k] = v;
        }
        properties.Tags[NextRotationOnTag] = value.NextRotationOn.ToString("O");
        properties.Tags["ChangedBy"] = "secret-manager.exe";
        // Tags to appease the old secret management system
        properties.Tags["Owner"] = "secret-manager.exe";
        properties.Tags["SecretType"] = "MANAGED";
        properties.ExpiresOn = value.ExpiresOn;
        await client.UpdateSecretPropertiesAsync(properties);
    }

    public override async Task EnsureKeyAsync(AzureKeyVaultParameters parameters, string name, SecretManifest.Key config)
    {
        var client = await CreateKeyClient(parameters);
        try
        {
            await client.GetKeyAsync(name);
            return; // key exists, so we are done.
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
        }

        switch (config.Type.ToLowerInvariant())
        {
            case "rsa":
                await client.CreateKeyAsync(name, KeyType.Rsa, new CreateRsaKeyOptions(name)
                {
                    KeySize = config.Size,
                });
                break;
            default:
                throw new NotImplementedException(config.Type);
        }
    }
}
