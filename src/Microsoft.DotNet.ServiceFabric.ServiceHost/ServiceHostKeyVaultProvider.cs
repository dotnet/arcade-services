// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Azure.KeyVault;
using Microsoft.Azure.Services.AppAuthentication;
using Microsoft.DotNet.Configuration.Extensions;
using Microsoft.Extensions.Hosting;

namespace Microsoft.DotNet.ServiceFabric.ServiceHost
{
    public class ServiceHostKeyVaultProvider : IKeyVaultProvider
    {
        private readonly IHostingEnvironment _env;

        public ServiceHostKeyVaultProvider(IHostingEnvironment env)
        {
            _env = env;
        }

        public KeyVaultClient CreateKeyVaultClient()
        {
            return CreateKeyVaultClient(_env);
        }

        public static KeyVaultClient CreateKeyVaultClient(IHostingEnvironment hostingEnvironment)
        {
            string connectionString = ServiceHostConfiguration.GetAzureServiceTokenProviderConnectionString(hostingEnvironment);
            var provider = new AzureServiceTokenProvider(connectionString);
            return new KeyVaultClient(new KeyVaultClient.AuthenticationCallback(provider.KeyVaultTokenCallback));
        }
    }
}