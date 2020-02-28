using System;
using System.Collections.Generic;
using System.Diagnostics;
using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
#if NETCOREAPP2_1
using IHostEnvironment = Microsoft.Extensions.Hosting.IHostingEnvironment;
#else
using IHostEnvironment = Microsoft.Extensions.Hosting.IHostEnvironment;
#endif

namespace Microsoft.DncEng.Configuration.Extensions
{
    public static class ServiceConfigurationExtensions
    {
        public static IServiceCollection AddDefaultJsonConfiguration(this IServiceCollection services)
        {
            return services.AddSingleton(provider =>
            {
                IHostEnvironment env = provider.GetRequiredService<IHostEnvironment>();
                IConfiguration config = new ConfigurationBuilder()
                    .AddDefaultJsonConfiguration(env)
                    .Build();
                return config;
            });
        }

        /// <summary>
        ///   Adds the "Default" json file configuration to the <see cref="IConfigurationBuilder"/>.
        ///   This loads the files '.config/settings.json' and '.config/settings.&lt;Environment&gt;.json', and maps Key Vault secrets and Azure App Configuration keys into the configuration.
        ///   Key Vault:
        ///     Key Vault secrets are read from the Key Vault referenced by the '<see cref="ConfigurationConstants.KeyVaultUriConfigurationKey"/>' configuration value and used to replace any references of the form '[vault(my_special_secret_name)]' with the corresponding secret value
        ///   App Configuration:
        ///     App Configuration keys are retrieved from the '<see cref="ConfigurationConstants.AppConfigurationUriConfigurationKey"/>' configuration value and replace references matching '[config(my_app_config_key)]' with the key value
        ///
        ///   Authentication is handled by either MSI (optionally using the '<see cref="ConfigurationConstants.ManagedIdentityIdConfigurationKey"/>' configuration value for a user-assigned managed identity), or VS/az cli authentication.
        ///   Values will be refreshed every '<see cref="ConfigurationConstants.ReloadTimeSecondsConfigurationKey"/>' seconds.
        /// </summary>
        public static IConfigurationBuilder AddDefaultJsonConfiguration(this IConfigurationBuilder builder, IHostEnvironment hostEnvironment, string configPathFormat = ".config/settings{0}.json")
        {
            string rootConfigFile = string.Format(configPathFormat, "");
            string envConfigFile = string.Format(configPathFormat, "." + hostEnvironment.EnvironmentName);

            IConfiguration bootstrapConfig = new ConfigurationBuilder()
                .SetBasePath(hostEnvironment.ContentRootPath)
                .AddJsonFile(rootConfigFile)
                .AddJsonFile(envConfigFile)
                .Build();

            string reloadTimeString = bootstrapConfig[ConfigurationConstants.ReloadTimeSecondsConfigurationKey];
            if (!int.TryParse(reloadTimeString, out int reloadTimeSeconds))
            {
                reloadTimeSeconds = 5 * 60;
            }

            var reloadTime = TimeSpan.FromSeconds(reloadTimeSeconds);

            Func<string, string> keyVault = KeyVaultConfigMapper.Create(bootstrapConfig);
            Func<string, string> appConfiguration = AppConfigurationConfigMapper.Create(bootstrapConfig);

            string Mapper(string v) => keyVault(appConfiguration(v));

            return builder
                .AddMappedJsonFile(rootConfigFile, reloadTime, Mapper)
                .AddMappedJsonFile(envConfigFile, reloadTime, Mapper);
        }

        public static IConfigurationBuilder AddMappedJsonFile(this IConfigurationBuilder builder, string filePath, TimeSpan reloadTime, Func<string, string> mapFunc)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            if (string.IsNullOrEmpty(filePath))
            {
                throw new ArgumentException("Invalid File Path", nameof(filePath));
            }

            var source = new MappedJsonConfigurationSource(reloadTime, mapFunc)
            {
                Path = filePath,
                Optional = false,
                ReloadOnChange = false,
            };
            builder.Add(source);
            return builder;
        }

        public static TokenCredential GetAzureTokenCredential(IConfiguration configuration)
        {
            string userAssignedIdentityId = configuration[ConfigurationConstants.ManagedIdentityIdConfigurationKey];
            var credentials = new List<TokenCredential>
            {
                new ManagedIdentityCredential(userAssignedIdentityId),
            };
            if (Debugger.IsAttached)
            {
                credentials.Add(new LocalDevTokenCredential());
            }

            return new ChainedTokenCredential(credentials.ToArray());
        }
    }
}
