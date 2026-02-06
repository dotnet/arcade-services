// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Azure.Extensions.AspNetCore.Configuration.Secrets;
using Azure.Security.KeyVault.Secrets;

namespace ProductConstructionService.Api.Configuration;

internal class KeyVaultSecretsWithPrefix(string prefix) : KeyVaultSecretManager
{
    private readonly string _prefix = prefix;

    public override string GetKey(KeyVaultSecret secret)
        => _prefix + secret.Name.Replace("--", ConfigurationPath.KeyDelimiter);
}
