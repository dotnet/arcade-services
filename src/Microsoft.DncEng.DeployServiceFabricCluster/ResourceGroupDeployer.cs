using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Azure.Security.KeyVault.Secrets;
using Microsoft.Azure.Management.Fluent;
using Microsoft.Azure.Management.Network.Fluent;
using Microsoft.Azure.Management.Network.Fluent.Models;
using Microsoft.Azure.Management.ResourceManager.Fluent;
using Microsoft.Azure.Management.ResourceManager.Fluent.Models;
using Microsoft.DncEng.Configuration.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace Microsoft.DncEng.DeployServiceFabricCluster
{
    internal abstract class ResourceGroupDeployer<TResult, TSettings>
        where TSettings : ResourceGroupDeployerSettings
    {
        private readonly IConfiguration _config;
        public TSettings Settings { get; }
        public Dictionary<string, string> DefaultTags { get; } = new Dictionary<string, string>();
        public ILogger Logger { get; }

        public ResourceGroupDeployer(TSettings settings, ILogger logger, IConfiguration config)
        {
            _config = config;
            Settings = settings;
            Logger = logger;
        }

        protected virtual void PopulateDefaultTags()
        {
            DefaultTags["resourceType"] = "Service Fabric";
        }

        protected abstract Task<TResult> DeployResourcesAsync(List<IGenericResource> unexpectedResources, IAzure azure, IResourceManager resourceManager, CancellationToken cancellationToken);

        public virtual async Task<TResult> DeployAsync(CancellationToken cancellationToken)
        {
            PopulateDefaultTags();
            var (azure, resourceManager) = Helpers.Authenticate(Settings.SubscriptionId.ToString());
            if (!await resourceManager.ResourceGroups.ContainAsync(Settings.ResourceGroup, cancellationToken))
            {
                await resourceManager.ResourceGroups.Define(Settings.ResourceGroup)
                    .WithRegion(Settings.Location)
                    .WithTags(DefaultTags)
                    .CreateAsync(cancellationToken);
            }
            var unexpectedResources =
                (await resourceManager.GenericResources.ListByResourceGroupAsync(Settings.ResourceGroup, loadAllPages: true, cancellationToken: cancellationToken))
                .ToList();

            var result = await DeployResourcesAsync(unexpectedResources, azure, resourceManager, cancellationToken);

            foreach (IGenericResource resource in unexpectedResources)
            {
                Logger.LogWarning("Unexpected resource '{resourceId}' consider deleting it.", resource.Id);
            }

            return result;
        }

        protected string GetResourceId(ResourceReference reference, string resourceType)
        {
            var subscription = reference.SubscriptionId ?? Settings.SubscriptionId.ToString();
            var resourceGroup = reference.ResourceGroup ?? Settings.ResourceGroup;
            return $"/subscriptions/{subscription}/resourceGroups/{resourceGroup}/providers/{resourceType}/{reference.Name}";
        }

        protected string GetKeyVaultSecretId(string certificateName)
        {
            var credentials = ServiceConfigurationExtensions.GetAzureTokenCredential(_config);
            var keyVaultUri = new Uri($"https://{Settings.CertificateSourceVault.Name}.vault.azure.net/");
            var client = new SecretClient(keyVaultUri, credentials);
            return client.GetSecret(certificateName).Value.Id.AbsoluteUri;
        }

        protected List<string> GetKeyVaultSecretIds(string certificateName)
        {
            var credentials = ServiceConfigurationExtensions.GetAzureTokenCredential(_config);
            var keyVaultUri = new Uri($"https://{Settings.CertificateSourceVault.Name}.vault.azure.net/");
            var client = new SecretClient(keyVaultUri, credentials);
            return client.GetPropertiesOfSecretVersions(certificateName).Select(secretProps =>
            {
                if (secretProps.ExpiresOn.HasValue && secretProps.ExpiresOn.Value < DateTimeOffset.Now.AddDays(-1))
                {
                    return null!;
                }

                return secretProps.Id.AbsoluteUri;
            }).Where(n => n != null).ToList();
        }

        protected async Task<IPublicIPAddress> DeployPublicIp(ICollection<IGenericResource> unexpectedResources, IAzure azure, string name, string domainName, CancellationToken cancellationToken)
        {
            IGenericResource existingIp = unexpectedResources.FirstOrDefault(r =>
                string.Equals(r.ResourceProviderNamespace, "Microsoft.Network", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(r.ResourceType, "publicIPAddresses", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase));

            if (existingIp == null)
            {
                Logger.LogInformation("Creating new IP address {ipName}", name);

                return await azure.PublicIPAddresses.Define(name)
                    .WithRegion(Settings.Location)
                    .WithExistingResourceGroup(Settings.ResourceGroup)
                    .WithTags(DefaultTags)
                    .WithSku(PublicIPSkuType.Standard)
                    .WithStaticIP()
                    .WithLeafDomainLabel(domainName)
                    .CreateAsync(cancellationToken);
            }

            unexpectedResources.Remove(existingIp);

            Logger.LogInformation("Updating existing IP address {ipName}", name);
            return await (await azure.PublicIPAddresses.GetByResourceGroupAsync(Settings.ResourceGroup, name, cancellationToken))
                .Update()
                .WithTags(DefaultTags)
                .WithStaticIP()
                .WithLeafDomainLabel(domainName)
                .ApplyAsync(cancellationToken);
        }

        protected async Task<JObject> DeployTemplateAsync(string name, IResourceManager resourceManager, JObject template, CancellationToken cancellationToken)
        {
            template["$schema"] = "http://schema.management.azure.com/schemas/2015-01-01/deploymentTemplate.json";
            template["contentVersion"] = "1.0.0.0";

            Logger.LogInformation("Deploying template '{templateName}'", name);

            if (resourceManager.Deployments.CheckExistence(Settings.ResourceGroup, name))
            {
                await resourceManager.Deployments.DeleteByResourceGroupAsync(Settings.ResourceGroup, name, cancellationToken);
            }

            var deployment = await resourceManager.Deployments.Define(name)
                .WithExistingResourceGroup(Settings.ResourceGroup)
                .WithTemplate(template)
                .WithParameters(new JObject())
                .WithMode(DeploymentMode.Incremental)
                .CreateAsync(cancellationToken);

            await deployment.RefreshAsync(cancellationToken);

            return (JObject)deployment.Outputs;
        }
    }
}
