// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.IO;
using Microsoft.Azure.KeyVault;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace Microsoft.DotNet.Configuration.Extensions
{
    public static class KeyVaultMappedJsonConfigurationExtensions
    {
        public static IConfigurationBuilder AddKeyVaultMappedJsonFile(
            this IConfigurationBuilder builder,
            string path,
            string vaultUri,
            TimeSpan reloadTime,
            Func<KeyVaultClient> clientFactory)
        {
            return AddKeyVaultMappedJsonFile(builder, null, path, false, false, vaultUri, reloadTime, clientFactory);
        }

        public static IConfigurationBuilder AddKeyVaultMappedJsonFile(
            this IConfigurationBuilder builder,
            IFileProvider provider,
            string path,
            bool optional,
            bool reloadOnChange,
            string vaultUri,
            TimeSpan reloadTime,
            Func<KeyVaultClient> clientFactory)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            if (String.IsNullOrEmpty(path))
            {
                throw new ArgumentException("Invalid File Path", nameof(path));
            }

            if (provider == null && Path.IsPathRooted(path))
            {
                provider = new PhysicalFileProvider(Path.GetDirectoryName(path));
                path = Path.GetFileName(path);
            }

            var source = new KeyVaultMappedJsonConfigurationSource(clientFactory, vaultUri, reloadTime)
            {
                FileProvider = provider,
                Path = path,
                Optional = optional,
                ReloadOnChange = reloadOnChange
            };
            builder.Add(source);
            return builder;
        }

        public static IConfigurationRoot CreateConfiguration(IHostingEnvironment env, IKeyVaultProvider keyVaultClient, string configPathFormat = ".config/settings{0}json")
        {
            string rootConfigFile = string.Format(configPathFormat, "");
            string envConfigFile = string.Format(configPathFormat, "." + env.EnvironmentName);
            IConfigurationRoot bootstrapConfig = new ConfigurationBuilder().SetBasePath(env.ContentRootPath)
                .AddJsonFile(rootConfigFile)
                .AddJsonFile(envConfigFile)
                .Build();

            Func<KeyVaultClient> clientFactory = keyVaultClient.CreateKeyVaultClient;
            string keyVaultUri = bootstrapConfig["KeyVaultUri"];
            string reloadTimeString = bootstrapConfig["KeyVaultReloadTime"];
            if (!TimeSpan.TryParse(reloadTimeString, out var reloadTime))
            {
                reloadTime = TimeSpan.FromMinutes(5);
            }

            return new ConfigurationBuilder().SetBasePath(env.ContentRootPath)
                .AddKeyVaultMappedJsonFile(rootConfigFile, keyVaultUri, reloadTime, clientFactory)
                .AddKeyVaultMappedJsonFile(envConfigFile, keyVaultUri, reloadTime, clientFactory)
                .Build();
        }

        public static void AddKeyVaultMappedConfiguration(this IServiceCollection services)
        {
            services.AddSingleton(
                provider => CreateConfiguration(
                    provider.GetRequiredService<IHostingEnvironment>(),
                    provider.GetRequiredService<IKeyVaultProvider>()
                )
            );
        }
    }
}
