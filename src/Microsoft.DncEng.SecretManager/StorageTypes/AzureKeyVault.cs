using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Azure;
using Azure.Security.KeyVault.Secrets;
using Microsoft.DncEng.CommandLineLib;
using Microsoft.DncEng.CommandLineLib.Authentication;

namespace Microsoft.DncEng.SecretManager.StorageTypes
{
    [Name("azure-key-vault")]
    public class AzureKeyVault : StorageLocationType
    {
        private static readonly string _nextRotationOnTag = "next-rotation-on";
        private readonly TokenCredentialProvider _tokenCredentialProvider;
        private readonly Guid _subscription;
        private readonly string _vaultName;

        public AzureKeyVault(IReadOnlyDictionary<string, string> parameters, TokenCredentialProvider tokenCredentialProvider) : base(parameters)
        {
            _tokenCredentialProvider = tokenCredentialProvider;
            ReadRequiredParameter("subscription", ref _subscription);
            ReadRequiredParameter("name", ref _vaultName);
        }

        private async Task<SecretClient> CreateSecretClient()
        {
            var creds = await _tokenCredentialProvider.GetCredentialAsync();
            return new SecretClient(new Uri($"https://{_vaultName}.vault.azure.net/"), creds);
        }

        public override async Task<List<SecretProperties>> ListSecretsAsync()
        {
            SecretClient client = await CreateSecretClient();
            var secrets = new List<SecretProperties>();
            await foreach (var secret in client.GetPropertiesOfSecretsAsync())
            {
                DateTimeOffset nextRotationOn = GetNextRotationOn(secret.Tags);
                ImmutableDictionary<string, string> tags = GetTags(secret);
                secrets.Add(new SecretProperties(secret.Name, secret.ExpiresOn ?? DateTimeOffset.MaxValue, nextRotationOn, tags));
            }

            return secrets;
        }

        private static DateTimeOffset GetNextRotationOn(IDictionary<string, string> tags)
        {
            if (!tags.TryGetValue(_nextRotationOnTag, out var nextRotationOnString) ||
                !DateTimeOffset.TryParse(nextRotationOnString, out var nextRotationOn))
            {
                nextRotationOn = DateTimeOffset.MaxValue;
            }

            return nextRotationOn;
        }

        public override async Task<SecretValue> GetSecretValueAsync(string name)
        {
            SecretClient client = await CreateSecretClient();
            Response<KeyVaultSecret> res = await client.GetSecretAsync(name);
            KeyVaultSecret secret = res.Value;
            DateTimeOffset nextRotationOn = GetNextRotationOn(secret.Properties.Tags);
            ImmutableDictionary<string, string> tags = GetTags(secret.Properties);
            return new SecretValue(secret.Value, tags, nextRotationOn, secret.Properties.ExpiresOn ?? DateTimeOffset.MaxValue);
        }

        private static ImmutableDictionary<string, string> GetTags(global::Azure.Security.KeyVault.Secrets.SecretProperties properties)
        {
            ImmutableDictionary<string, string> tags = properties.Tags.Where(p => p.Key != _nextRotationOnTag)
                .ToImmutableDictionary();
            return tags;
        }

        public override async Task SetSecretValueAsync(string name, SecretValue value)
        {
            SecretClient client = await CreateSecretClient();
            var createdSecret = await client.SetSecretAsync(name, value.Value);
            var properties = createdSecret.Value.Properties;
            foreach (var (k, v) in value.Tags)
            {
                properties.Tags[k] = v;
            }
            properties.Tags[_nextRotationOnTag] = value.NextRotationOn.ToString("O");
            properties.Tags["ChangedBy"] = "secret-manager.exe";
            properties.ExpiresOn = value.ExpiresOn;
            await client.UpdateSecretPropertiesAsync(properties);
        }
    }
}
