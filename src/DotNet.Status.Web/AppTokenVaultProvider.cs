// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Azure.KeyVault;
using Microsoft.Azure.Services.AppAuthentication;
using Microsoft.DotNet.Configuration.Extensions;

namespace DotNet.Status.Web
{
    public class AppTokenVaultProvider : IKeyVaultProvider
    {
        public KeyVaultClient CreateKeyVaultClient()
        {
            var tokenProvider = new AzureServiceTokenProvider();
            return new KeyVaultClient(new KeyVaultClient.AuthenticationCallback(tokenProvider.KeyVaultTokenCallback));
        }
    }
}
