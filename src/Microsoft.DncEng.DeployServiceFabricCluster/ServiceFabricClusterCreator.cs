using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http.Headers;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Azure.Storage;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Azure.Management.Fluent;
using Microsoft.Azure.Management.Network.Fluent;
using Microsoft.Azure.Management.Network.Fluent.LoadBalancer.Definition;
using Microsoft.Azure.Management.Network.Fluent.Models;
using Microsoft.Azure.Management.Network.Fluent.Network.Definition;
using Microsoft.Azure.Management.ResourceManager.Fluent;
using Microsoft.Azure.Management.ResourceManager.Fluent.Authentication;
using Microsoft.Azure.Management.ResourceManager.Fluent.Core;
using Microsoft.Azure.Management.ResourceManager.Fluent.Models;
using Microsoft.Azure.Management.Storage.Fluent;
using Microsoft.Azure.Management.Storage.Fluent.Models;
using Microsoft.Azure.Services.AppAuthentication;
using Microsoft.Extensions.Logging;
using Microsoft.Rest;
using Microsoft.Rest.Azure;
using Microsoft.Rest.TransientFaultHandling;
using Newtonsoft.Json.Linq;
using IUpdate = Microsoft.Azure.Management.Network.Fluent.NetworkSecurityGroup.Update.IUpdate;
using IWithCreate = Microsoft.Azure.Management.Network.Fluent.NetworkSecurityGroup.Definition.IWithCreate;

namespace Microsoft.DncEng.DeployServiceFabricCluster
{
    internal class ServiceFabricClusterCreator
    {
        private const string MsftAdTenantId = "72f988bf-86f1-41af-91ab-2d7cd011db47";

        private static readonly AzureServiceTokenProvider TokenProvider = new AzureServiceTokenProvider();

        private readonly ILogger _logger;
        private readonly ServiceFabricClusterConfiguration _config;

        private IDictionary<string, string> DefaultTags => new Dictionary<string, string>
        {
            ["resourceType"] = "Service Fabric",
            ["clusterName"] = _config.Name,
        };

        public ServiceFabricClusterCreator(ServiceFabricClusterConfiguration config, ILogger logger)
        {
            _config = config;
            _logger = logger;
        }

        private class AzureCredentialsTokenProvider : ITokenProvider
        {
            private readonly AzureServiceTokenProvider _inner;

            public AzureCredentialsTokenProvider(AzureServiceTokenProvider inner)
            {
                _inner = inner;
            }

            public async Task<AuthenticationHeaderValue> GetAuthenticationHeaderAsync(CancellationToken cancellationToken)
            {
                string token = await _inner.GetAccessTokenAsync("https://management.azure.com", MsftAdTenantId);
                return new AuthenticationHeaderValue("Bearer", token);
            }
        }

        private (IAzure, IResourceManager) Authenticate()
        {
            string version = Assembly.GetEntryAssembly()?
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                .InformationalVersion ?? "1.0.0";

            var tokenCredentials = new TokenCredentials(new AzureCredentialsTokenProvider(TokenProvider));
            var credentials = new AzureCredentials(tokenCredentials, null, MsftAdTenantId, AzureEnvironment.AzureGlobalCloud);

            HttpLoggingDelegatingHandler.Level logLevel = HttpLoggingDelegatingHandler.Level.Headers;
            var retryPolicy = new RetryPolicy(new DefaultTransientErrorDetectionStrategy(), 5);
            var programName = "DncEng Service Fabric Cluster Creator";

            return (Azure.Management.Fluent.Azure.Configure()
                    .WithLogLevel(logLevel)
                    .WithRetryPolicy(retryPolicy)
                    .WithUserAgent(programName, version)
                    .Authenticate(credentials)
                    .WithSubscription(_config.SubscriptionId.ToString()),
                ResourceManager.Configure()
                    .WithLogLevel(logLevel)
                    .WithRetryPolicy(retryPolicy)
                    .WithUserAgent(programName, version)
                    .Authenticate(credentials)
                    .WithSubscription(_config.SubscriptionId.ToString()));
        }

        public async Task CreateClusterAsync(CancellationToken cancellationToken)
        {
            var (azure, resourceManager) = Authenticate();

            var (artifactStorageClient, artifactsLocationInfo) = await EnsureArtifactStorage(azure, cancellationToken);
            string artifactsLocation = artifactStorageClient.Uri.AbsoluteUri;

            string searchDir = Path.Join(AppContext.BaseDirectory, "scripts");
            foreach (string file in Directory.EnumerateFiles(searchDir, "*", SearchOption.AllDirectories))
            {
                string relative = file.Substring(searchDir.Length + 1);
                await using var stream = new FileStream(file, FileMode.Open, FileAccess.Read);
                await artifactStorageClient.DeleteBlobIfExistsAsync(relative, cancellationToken: cancellationToken);
                await artifactStorageClient.UploadBlobAsync(relative, stream, cancellationToken);
            }


            if (!await azure.ResourceGroups.ContainAsync(_config.ResourceGroup, cancellationToken))
            {
                await azure.ResourceGroups.Define(_config.ResourceGroup)
                    .WithRegion(_config.Location)
                    .WithTags(DefaultTags)
                    .CreateAsync(cancellationToken);
            }

            var unexpectedResources =
                (await resourceManager.GenericResources.ListByResourceGroupAsync(_config.ResourceGroup, loadAllPages: true, cancellationToken: cancellationToken))
                .ToList();

            IgnoreResources(unexpectedResources, new[]
            {
                ("microsoft.alertsManagement", "*"),
                ("Microsoft.Insights", "metricalerts"),
                ("Microsoft.Insights", "webtests"),
                ("Microsoft.Storage", "storageAccounts"),
            });

            var (supportLogStorage, applicationDiagnosticsStorage) = await EnsureStorageAccounts(azure, cancellationToken);

            string instrumentationKey = await DeployApplicationInsights(unexpectedResources, resourceManager, cancellationToken);

            INetworkSecurityGroup nsg = await DeployNetworkSecurityGroup(unexpectedResources, azure, cancellationToken);

            IReadOnlyList<IPublicIPAddress> publicIps = await DeployPublicIps(unexpectedResources, azure, cancellationToken);

            INetwork vnet = await DeployVirtualNetwork(unexpectedResources, azure, nsg, cancellationToken);

            string clusterEndpoint = await DeployServiceFabric(unexpectedResources, resourceManager, publicIps.First(), supportLogStorage, cancellationToken);

            foreach (var (nodeType, ip) in _config.NodeTypes.Zip(publicIps))
            {
                ISubnet subnet = vnet.Subnets["Node-" + nodeType.Name];

                ILoadBalancer lb = await DeployLoadBalancer(unexpectedResources, azure, nodeType, ip, cancellationToken);

                await DeployScaleSet(unexpectedResources, resourceManager, nodeType, lb, subnet, clusterEndpoint, instrumentationKey, artifactsLocation, artifactsLocationInfo, supportLogStorage, applicationDiagnosticsStorage, cancellationToken);
            }

            foreach (IGenericResource resource in unexpectedResources)
            {
                _logger.LogWarning("Unexpected resource '{resourceId}' consider deleting it.", resource.Id);
            }
        }

        private async Task<(StorageAccountInfo supportLogStorage, StorageAccountInfo applicationDiagnosticsStorage)> EnsureStorageAccounts(IAzure azure, CancellationToken cancellationToken)
        {
            StorageAccountInfo[] accounts = await Task.WhenAll(
                EnsureStorageAccount(azure, _config.ResourceGroup, "sflogs" + _config.SubscriptionId.ToString("N").Substring(0, 18), cancellationToken),
                EnsureStorageAccount(azure, _config.ResourceGroup, "sfdg" + _config.SubscriptionId.ToString("N").Substring(0, 20), cancellationToken)
            );
            return (accounts[0], accounts[1]);
        }

        private class StorageAccountInfo
        {
            public StorageAccountInfo(string name, string key, BlobServiceClient client, IStorageAccount storageAccount)
            {
                Name = name;
                Key = key;
                Client = client;
                StorageAccount = storageAccount;
            }

            public BlobServiceClient Client { get; }
            public IStorageAccount StorageAccount { get; }
            public string Name { get; }
            public string Key { get; }
        }

        private async Task<StorageAccountInfo> EnsureStorageAccount(IAzure azure, string resourceGroupName, string storageAccountName, CancellationToken cancellationToken)
        {
            _logger.LogInformation("Ensuring storage account '{accountName}' exists.", storageAccountName);

            IStorageAccount storageAccount = (await azure.StorageAccounts.ListAsync(true, cancellationToken)).FirstOrDefault(s => s.Name == storageAccountName);
            if (storageAccount == null)
            {
                IResourceGroup resourceGroup = await azure.ResourceGroups.GetByNameAsync(resourceGroupName, cancellationToken);
                if (resourceGroup == null)
                {
                    resourceGroup = await azure.ResourceGroups.Define(resourceGroupName)
                        .WithRegion(_config.Location)
                        .CreateAsync(cancellationToken);
                }

                storageAccount = await azure.StorageAccounts.Define(storageAccountName)
                    .WithRegion(_config.Location)
                    .WithExistingResourceGroup(resourceGroup)
                    .WithSku(StorageAccountSkuType.Standard_LRS)
                    .WithTags(DefaultTags)
                    .CreateAsync(cancellationToken);
            }

            StorageAccountKey key = (await storageAccount.GetKeysAsync(cancellationToken)).First();

            var credential = new StorageSharedKeyCredential(storageAccount.Name, key.Value);

            var account = new BlobServiceClient(new Uri(storageAccount.Inner.PrimaryEndpoints.Blob), credential);

            return new StorageAccountInfo(storageAccountName, key.Value, account, storageAccount);
        }

        private async Task<(BlobContainerClient container, StorageAccountInfo account)> EnsureArtifactStorage(IAzure azure, CancellationToken cancellationToken)
        {
            string storageAccountName = "stage" + _config.SubscriptionId.ToString("N").Substring(0, 19);
            StorageAccountInfo accountInfo = await EnsureStorageAccount(azure, "ARM_Deploy_Staging", storageAccountName, cancellationToken);

            BlobContainerClient container = accountInfo.Client.GetBlobContainerClient(_config.Name + "-stageartifacts");
            await container.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: cancellationToken);

            return (container, accountInfo);
        }

        private async Task DeployScaleSet(ICollection<IGenericResource> unexpectedResources,
            IResourceManager resourceManager, ServiceFabricNodeType nodeType, ILoadBalancer lb, ISubnet subnet, string clusterEndpoint,
            string instrumentationKey,
            string artifactsLocation,
            StorageAccountInfo artifactsLocationInfo,
            StorageAccountInfo supportLogStorage,
            StorageAccountInfo applicationDiagnosticsStorage,
            CancellationToken cancellationToken)
        {
            string scaleSetName = $"{_config.Name}-{nodeType.Name}";

            IGenericResource scaleSetResource = unexpectedResources.FirstOrDefault(r =>
                r.ResourceProviderNamespace == "Microsoft.Compute" &&
                r.ResourceType == "virtualMachineScaleSets" &&
                r.Name == scaleSetName);

            if (scaleSetResource != null)
            {
                unexpectedResources.Remove(scaleSetResource);
            }

            await DeployTemplateAsync("Nodes-" + nodeType.Name, resourceManager, new JObject
            {
                ["resources"] = new JArray
                {
                    new JObject
                    {
                        ["apiVersion"] = "2018-06-01",
                        ["type"] = "Microsoft.Compute/virtualMachineScaleSets",
                        ["name"] = scaleSetName,
                        ["location"] = _config.Location,
                        ["identity"] = string.IsNullOrEmpty(nodeType.UserAssignedIdentityId) ? new JObject
                        {
                            ["type"] =  "SystemAssigned",
                        } : new JObject
                        {
                            ["type"] = "userAssigned",
                            ["userAssignedIdentities"] = new JObject
                            {
                                [nodeType.UserAssignedIdentityId] = new JObject{},
                            },
                        },
                        ["properties"] = new JObject
                        {
                            ["overprovision"] = false,
                            ["upgradePolicy"] = new JObject
                            {
                                ["mode"] = "Automatic",
                            },
                            ["virtualMachineProfile"] = new JObject
                            {
                                ["extensionProfile"] = new JObject
                                {
                                    ["extensions"] = new JArray
                                    {
                                        new JObject
                                        {
                                            ["name"] = nodeType.Name + "_ServiceFabricNode",
                                            ["properties"] = new JObject
                                            {
                                                ["publisher"] = "Microsoft.Azure.ServiceFabric",
                                                ["type"] = "ServiceFabricNode",
                                                ["typeHandlerVersion"] = "1.0",
                                                ["autoUpgradeMinorVersion"] = true,
                                                ["protectedSettings"] = new JObject
                                                {
                                                    ["StorageAccountKey1"] = supportLogStorage.Key,
                                                },
                                                ["settings"] = new JObject
                                                {
                                                    ["clusterEndpoint"] = clusterEndpoint,
                                                    ["nodeTypeRef"] = nodeType.Name,
                                                    ["dataPath"] = "D:\\SvcFab",
                                                    ["durabilityLevel"] = "Silver",
                                                    ["enableParallelJobs"] = true,
                                                    ["nicPrefixOverride"] = subnet.AddressPrefix,
                                                    ["certificate"] = new JObject
                                                    {
                                                        ["commonNames"] = new JArray
                                                        {
                                                            _config.CertificateCommonName,
                                                        },
                                                        ["x509StoreName"] = "My",
                                                    },
                                                },
                                            },
                                        },
                                        new JObject
                                        {
                                            ["name"] = nodeType.Name + "_VMDiagnostics",
                                            ["properties"] = new JObject
                                            {
                                                ["publisher"] = "Microsoft.Azure.Diagnostics",
                                                ["type"] = "IaaSDiagnostics",
                                                ["typeHandlerVersion"] = "1.5",
                                                ["autoUpgradeMinorVersion"] = true,
                                                ["protectedSettings"] = new JObject
                                                {
                                                    ["storageAccountName"] = applicationDiagnosticsStorage.Name,
                                                    ["storageAccountKey"] = applicationDiagnosticsStorage.Key,
                                                    ["storageAccountEndPoint"] = "https://core.windows.net",
                                                },
                                                ["settings"] = new JObject
                                                {
                                                    ["WadCfg"] = new JObject
                                                    {
                                                        ["DiagnosticMonitorConfiguration"] = new JObject
                                                        {
                                                            ["overallQuotaInMB"] = "50000",
                                                            ["sinks"] = "applicationInsights",
                                                            ["EtwProviders"] = new JObject
                                                            {
                                                                ["EtwEventSourceProviderConfiguration"] = new JArray
                                                                {
                                                                    new JObject
                                                                    {
                                                                        ["provider"] = "Microsoft-ServiceFabric-Actors",
                                                                        ["scheduledTransferKeywordFilter"] = "1",
                                                                        ["scheduledTransferPeriod"] = "PT5M",
                                                                        ["DefaultEvents"] = new JObject
                                                                        {
                                                                            ["eventDestination"] = "ServiceFabricReliableActorEventTable",
                                                                        },
                                                                    },
                                                                    new JObject
                                                                    {
                                                                        ["provider"] = "Microsoft-ServiceFabric-Services",
                                                                        ["scheduledTransferKeywordFilter"] = "1",
                                                                        ["scheduledTransferPeriod"] = "PT5M",
                                                                        ["DefaultEvents"] = new JObject
                                                                        {
                                                                            ["eventDestination"] = "ServiceFabricReliableServiceEventTable",
                                                                        },
                                                                    },
                                                                },
                                                                ["EtwManifestProviderConfiguration"] = new JArray
                                                                {
                                                                    new JObject
                                                                    {
                                                                        ["provider"] = "cbd93bc2-71e5-4566-b3a7-595d8eeca6e8",
                                                                        ["scheduledTransferLogLevelFilter"] = "Information",
                                                                        ["scheduledTransferKeywordFilter"] = "4611686018427387904",
                                                                        ["scheduledTransferPeriod"] = "PT5M",
                                                                        ["DefaultEvents"] = new JObject
                                                                        {
                                                                            ["eventDestination"] = "ServiceFabricSystemEventTable",
                                                                        },
                                                                    },
                                                                },
                                                            },
                                                        },
                                                        ["SinksConfig"] = new JObject
                                                        {
                                                            ["Sink"] = new JArray
                                                            {
                                                                new JObject
                                                                {
                                                                    ["name"] = "applicationInsights",
                                                                    ["ApplicationInsights"] = instrumentationKey,
                                                                },
                                                            },
                                                        },
                                                    },
                                                    ["StorageAccount"] = applicationDiagnosticsStorage.Name,
                                                },
                                            },
                                        },
                                        new JObject
                                        {
                                            ["name"] = nodeType.Name + "_Setup",
                                            ["properties"] = new JObject
                                            {
                                                ["publisher"] = "Microsoft.Compute",
                                                ["type"] = "CustomScriptExtension",
                                                ["typeHandlerVersion"] = "1.9",
                                                ["autoUpgradeMinorVersion"] = true,
                                                ["protectedSettings"] = new JObject
                                                {
                                                    ["storageAccountName"] = artifactsLocationInfo.Name,
                                                    ["storageAccountKey"] = artifactsLocationInfo.Key,
                                                },
                                                ["settings"] = new JObject
                                                {
                                                    ["fileUris"] = new JArray
                                                    {
                                                        artifactsLocation + "/startup.ps1",
                                                        artifactsLocation + "/Set-TlsConfiguration.ps1",
                                                    },
                                                    ["commandToExecute"] = $"powershell.exe -ExecutionPolicy Bypass -NoProfile -Command ./startup.ps1 \"\"\"{instrumentationKey}\"\"\"",
                                                },
                                            },
                                        },
                                    },
                                },
                                ["networkProfile"] = new JObject
                                {
                                    ["networkInterfaceConfigurations"] = new JArray
                                    {
                                        new JObject
                                        {
                                            ["name"] = $"NIC-{nodeType.Name}-1",
                                            ["properties"] = new JObject
                                            {
                                                ["primary"] = true,
                                                ["enableAcceleratedNetworking"] = true,
                                                ["ipConfigurations"] = new JArray
                                                {
                                                    new JObject
                                                    {
                                                        ["name"] = $"NIC-{nodeType.Name}-1",
                                                        ["properties"] = new JObject
                                                        {
                                                            ["loadBalancerBackendAddressPools"] = new JArray
                                                            {
                                                                new JObject
                                                                {
                                                                    ["id"] = lb.Backends.First().Value.Inner.Id,
                                                                },
                                                            },
                                                            ["subnet"] = new JObject
                                                            {
                                                                ["id"] = subnet.Inner.Id,
                                                            },
                                                        },
                                                    },
                                                },
                                            },
                                        },
                                    },
                                },
                                ["osProfile"] = new JObject
                                {
                                    ["adminUsername"] = _config.AdminUsername,
                                    ["adminPassword"] = _config.AdminPassword,
                                    ["computernamePrefix"] = nodeType.Name,
                                    ["secrets"] = new JArray
                                    {
                                        new JObject
                                        {
                                            ["sourceVault"] = new JObject
                                            {
                                                ["id"] = _config.CertificateSourceVaultId,
                                            },
                                            ["vaultCertificates"] = new JArray(
                                                _config.CertificateUrls.Select(u => new JObject
                                                {
                                                    ["certificateUrl"] = u,
                                                    ["certificateStore"] = "My",
                                                })),
                                        },
                                    },
                                },
                                ["storageProfile"] = new JObject
                                {
                                    ["imageReference"] = new JObject
                                    {
                                        ["publisher"] = nodeType.VmImage.Publisher,
                                        ["offer"] = nodeType.VmImage.Offer,
                                        ["sku"] = nodeType.VmImage.Sku,
                                        ["version"] = nodeType.VmImage.Version,
                                    },
                                    ["osDisk"] = new JObject
                                    {
                                        ["caching"] = "ReadOnly",
                                        ["createOption"] = "FromImage",
                                        ["diffDiskSettings"] = new JObject
                                        {
                                            ["option"] = "Local",
                                        },
                                    },
                                },
                            },
                        },
                        ["sku"] = new JObject
                        {
                            ["name"] = nodeType.Sku,
                            ["capacity"] = nodeType.InstanceCount,
                            ["tier"] = "Standard",
                        },
                        ["tags"] = JObject.FromObject(new Dictionary<string, string>(DefaultTags)
                        {
                            ["SkipASMAzSecPack"] = "true",
                        }),
                    },
                },
            }, cancellationToken);
        }

        private async Task<string> DeployServiceFabric(ICollection<IGenericResource> unexpectedResources,
            IResourceManager resourceManager, IPublicIPAddress primaryIp, StorageAccountInfo supportLogStorage,
            CancellationToken cancellationToken)
        {
            IGenericResource svcFabResource = unexpectedResources.FirstOrDefault(r =>
                r.ResourceProviderNamespace == "Microsoft.ServiceFabric" &&
                r.ResourceType == "clusters" &&
                r.Name == _config.Name);

            if (svcFabResource != null)
            {
                unexpectedResources.Remove(svcFabResource);

                while (true)
                {
                    IGenericResource svcFab = await resourceManager.GenericResources.GetByIdAsync(svcFabResource.Id, cancellationToken: cancellationToken);
                    var props = (JObject) svcFab.Properties;
                    string state = props["clusterState"]?.Value<string>() ?? "Ready";

                    if (state == "Ready" ||
                        state == "WaitingForNodes")
                    {
                        break;
                    }

                    _logger.LogInformation("Service Fabric Resource is in state '{state}', cannot deploy yet.", state);
                    await Task.Delay(TimeSpan.FromSeconds(60), cancellationToken);
                }
            }

            try
            {
                JObject outputs = await DeployTemplateAsync("ServiceFabric", resourceManager, new JObject
                {
                    ["resources"] = new JArray
                    {
                        new JObject
                        {
                            ["apiVersion"] = "2018-02-01",
                            ["type"] = "Microsoft.ServiceFabric/clusters",
                            ["name"] = _config.Name,
                            ["location"] = _config.Location,
                            ["tags"] = JObject.FromObject(DefaultTags),
                            ["properties"] = new JObject
                            {
                                ["addonFeatures"] = new JArray(),
                                ["certificateCommonNames"] = new JObject
                                {
                                    ["commonNames"] = new JArray
                                    {
                                        new JObject
                                        {
                                            ["certificateCommonName"] = _config.CertificateCommonName,
                                            ["certificateIssuerThumbprint"] = "",
                                        },
                                    },
                                    ["x509StoreName"] = "My",
                                },
                                ["clientCertificateCommonNames"] = new JArray
                                {
                                    new JObject
                                    {
                                        ["certificateCommonName"] = _config.AdminClientCertificateCommonName,
                                        ["certificateIssuerThumbprint"] =
                                            _config.AdminClientCertificateIssuerThumbprint,
                                        ["isAdmin"] = true,
                                    },
                                },
                                ["diagnosticsStorageAccountConfig"] = new JObject
                                {
                                    ["blobEndpoint"] = supportLogStorage.StorageAccount.EndPoints.Primary.Blob,
                                    ["protectedAccountKeyName"] = "StorageAccountKey1",
                                    ["queueEndpoint"] = supportLogStorage.StorageAccount.EndPoints.Primary.Queue,
                                    ["storageAccountName"] = supportLogStorage.Name,
                                    ["tableEndpoint"] = supportLogStorage.StorageAccount.EndPoints.Primary.Table,
                                },
                                ["fabricSettings"] = new JArray
                                {
                                    new JObject
                                    {
                                        ["parameters"] = new JArray
                                        {
                                            new JObject
                                            {
                                                ["name"] = "ClusterProtectionLevel",
                                                ["value"] = "EncryptAndSign",
                                            },
                                        },
                                        ["name"] = "Security",
                                    },
                                    new JObject
                                    {
                                        ["parameters"] = new JArray
                                        {
                                            new JObject
                                            {
                                                ["name"] = "EnableDefaultServicesUpgrade",
                                                ["value"] = "true",
                                            },
                                        },
                                        ["name"] = "ClusterManager",
                                    },
                                },
                                ["managementEndpoint"] = $"https://{primaryIp.Fqdn}:{_config.HttpGatewayPort}",
                                ["reliabilityLevel"] = "Silver",
                                ["upgradeMode"] = "Automatic",
                                ["vmImage"] = "Windows",
                                ["nodeTypes"] = new JArray(
                                    _config.NodeTypes.Select(nt => new JObject
                                    {
                                        ["name"] = nt.Name,
                                        ["applicationPorts"] = new JObject
                                        {
                                            ["startPort"] = 20000,
                                            ["endPort"] = 30000,
                                        },
                                        ["clientConnectionEndpointPort"] = _config.TcpGatewayPort,
                                        ["durabilityLevel"] = "Silver",
                                        ["ephemeralPorts"] = new JObject
                                        {
                                            ["startPort"] = 49152,
                                            ["endPort"] = 65534,
                                        },
                                        ["httpGatewayEndpointPort"] = _config.HttpGatewayPort,
                                        ["isPrimary"] = nt.Name == "Primary",
                                        ["vmInstanceCount"] = nt.InstanceCount,
                                    })),
                            },
                        },
                    },
                    ["outputs"] = new JObject
                    {
                        ["clusterEndpoint"] = new JObject
                        {
                            ["value"] =
                                $"[reference(resourceId('Microsoft.ServiceFabric/clusters', '{_config.Name}')).clusterEndpoint]",
                            ["type"] = "string",
                        },
                    },
                }, cancellationToken);

                return outputs.Value<JObject>("clusterEndpoint").Value<string>("value");
            }
            catch (CloudException ex)
            {
                if (svcFabResource != null)
                {
                    // retrieve the properties
                    IGenericResource svcFab = await resourceManager.GenericResources.GetByIdAsync(svcFabResource.Id, cancellationToken: cancellationToken);

                    string endpoint = ((JObject) svcFab.Properties).Value<string>("clusterEndpoint");
                    if (!string.IsNullOrEmpty(endpoint))
                    {
                        _logger.LogWarning(ex, "Error deploying Service Fabric: {message}", ex.Message);
                        _logger.LogInformation("Service Fabric exists and has a cluster endpoint, proceeding to VM deployment");
                        return endpoint;
                    }
                }

                throw;
            }
        }

        private async Task<ILoadBalancer> DeployLoadBalancer(ICollection<IGenericResource> unexpectedResources, IAzure azure, ServiceFabricNodeType nodeType, IPublicIPAddress publicIp, CancellationToken cancellationToken)
        {
            string lbName = $"{_config.Name}-{nodeType.Name}-LB";

            IGenericResource existingLoadBalancer = unexpectedResources.FirstOrDefault(r =>
                string.Equals(r.ResourceProviderNamespace, "Microsoft.Network", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(r.ResourceType, "loadBalancers", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(r.Name, lbName, StringComparison.OrdinalIgnoreCase));

            var neededProbesAndRules = new List<(string name, int externalPort, int internalPort)>();

            if (nodeType.Name == "Primary")
            {
                neededProbesAndRules.AddRange(new[]
                {
                    ("FabricTcpGateway", _config.TcpGatewayPort, _config.TcpGatewayPort),
                    ("FabricHttpGateway", _config.HttpGatewayPort, _config.HttpGatewayPort),
                });
            }

            neededProbesAndRules.AddRange(nodeType.Endpoints
                .Select((ep, i) => ($"App-{i}", ep.ExternalPort, ep.InternalPort)));

            if (existingLoadBalancer == null)
            {
                _logger.LogInformation("Creating new load balancer {lbName}", lbName);

                IWithLBRuleOrNat def = azure.LoadBalancers.Define(lbName)
                    .WithRegion(_config.Location)
                    .WithExistingResourceGroup(_config.ResourceGroup);

                foreach (var (name, externalPort, internalPort) in neededProbesAndRules)
                {
                    def.DefineLoadBalancingRule(name + "-Rule")
                        .WithProtocol(TransportProtocol.Tcp)
                        .FromExistingPublicIPAddress(publicIp)
                        .FromFrontendPort(externalPort)
                        .ToBackend("LoadBalancerBEAddressPool")
                        .ToBackendPort(internalPort)
                        .WithProbe(name + "-Probe")
                        .Attach();
                }

                foreach (var (name, _, internalPort) in neededProbesAndRules)
                {
                    ((IWithProbe)def).DefineTcpProbe(name + "-Probe")
                        .WithPort(internalPort)
                        .WithIntervalInSeconds(5)
                        .WithNumberOfProbes(2)
                        .Attach();
                }

                return await ((IWithLBRuleOrNatOrCreate) def)
                    .WithTags(DefaultTags)
                    .WithSku(LoadBalancerSkuType.Standard)
                    .CreateAsync(cancellationToken);
            }

            _logger.LogInformation("Updating existing load balancer {lbName}", lbName);

            unexpectedResources.Remove(existingLoadBalancer);

            ILoadBalancer existingLb = await azure.LoadBalancers.GetByIdAsync(existingLoadBalancer.Id, cancellationToken);
            Azure.Management.Network.Fluent.LoadBalancer.Update.IUpdate update = existingLb.Update();
            foreach (string rule in existingLb.LoadBalancingRules.Keys)
            {
                update.WithoutLoadBalancingRule(rule);
            }

            foreach (string probe in existingLb.TcpProbes.Keys)
            {
                update.WithoutProbe(probe);
            }

            foreach (var (name, externalPort, internalPort) in neededProbesAndRules)
            {
                update.DefineLoadBalancingRule(name + "-Rule")
                    .WithProtocol(TransportProtocol.Tcp)
                    .FromExistingPublicIPAddress(publicIp)
                    .FromFrontendPort(externalPort)
                    .ToBackend("LoadBalancerBEAddressPool")
                    .ToBackendPort(internalPort)
                    .WithProbe(name + "-Probe")
                    .Attach();
            }

            foreach (var (name, _, internalPort) in neededProbesAndRules)
            {
                update.DefineTcpProbe(name + "-Probe")
                    .WithPort(internalPort)
                    .WithIntervalInSeconds(5)
                    .WithNumberOfProbes(2)
                    .Attach();
            }

            update.WithTags(DefaultTags);

            return await update.ApplyAsync(cancellationToken);
        }

        private async Task<INetwork> DeployVirtualNetwork(ICollection<IGenericResource> unexpectedResources, IAzure azure, INetworkSecurityGroup nsg, CancellationToken cancellationToken)
        {
            string vnetName = _config.Name + "-vnet";

            IGenericResource existingNetworkResource = unexpectedResources.FirstOrDefault(r =>
                string.Equals(r.ResourceProviderNamespace, "Microsoft.Network", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(r.ResourceType, "virtualNetworks", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(r.Name, vnetName, StringComparison.OrdinalIgnoreCase));

            IEnumerable<(string, string)> neededSubnets = new[]
            {
                ("AppGateway", "10.0.0.0/24"),
            }.Concat(_config.NodeTypes.Select((n, i) =>
                ("Node-" + n.Name, "10.0." + (i + 1) + ".0/24")));

            if (existingNetworkResource == null)
            {
                _logger.LogInformation("Creating new virtual network {vnetName}", vnetName);

                IWithCreateAndSubnet networkDef = azure.Networks.Define(vnetName)
                    .WithRegion(_config.Location)
                    .WithExistingResourceGroup(_config.ResourceGroup)
                    .WithAddressSpace("10.0.0.0/16");
                 foreach (var (name, prefix) in neededSubnets)
                 {
                     networkDef = networkDef.DefineSubnet(name)
                         .WithAddressPrefix(prefix)
                         .WithExistingNetworkSecurityGroup(nsg)
                         .Attach();
                 }

                 return await networkDef
                     .WithTags(DefaultTags)
                     .CreateAsync(cancellationToken);
            }

            _logger.LogInformation("Updating existing virtual network {vnetName}", vnetName);

            unexpectedResources.Remove(existingNetworkResource);

            INetwork existingNetwork = await azure.Networks.GetByIdAsync(existingNetworkResource.Id, cancellationToken);

            Azure.Management.Network.Fluent.Network.Update.IUpdate update = existingNetwork.Update();
            foreach (string space in existingNetwork.AddressSpaces)
            {
                update = update.WithoutAddressSpace(space);
            }
            foreach (KeyValuePair<string, ISubnet> subnet in existingNetwork.Subnets)
            {
                update = update.WithoutSubnet(subnet.Key);
            }

            update = update.WithAddressSpace("10.0.0.0/16");
            foreach (var (name, prefix) in neededSubnets)
            {
                update = update.DefineSubnet(name)
                    .WithAddressPrefix(prefix)
                    .WithExistingNetworkSecurityGroup(nsg)
                    .Attach();
            }

            update = update.WithTags(DefaultTags);

            return await update.ApplyAsync(cancellationToken);
        }

        private async Task<IReadOnlyList<IPublicIPAddress>> DeployPublicIps(ICollection<IGenericResource> unexpectedResources, IAzure azure, CancellationToken cancellationToken)
        {
            IEnumerable<(string name, string domainName)> expectedIps = _config.NodeTypes.Select((nt, i) =>
                (name: $"{_config.Name}-{nt.Name}-IP", domainName: _config.Name + (i == 0 ? "" : "-" + i)));

            var ips = new List<IPublicIPAddress>();

            foreach (var (name, domainName) in expectedIps)
            {
                IGenericResource existingIp = unexpectedResources.FirstOrDefault(r =>
                    string.Equals(r.ResourceProviderNamespace, "Microsoft.Network", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(r.ResourceType, "publicIPAddresses", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase));

                if (existingIp == null)
                {
                    _logger.LogInformation("Creating new IP address {ipName}", name);

                    ips.Add(await azure.PublicIPAddresses.Define(name)
                        .WithRegion(_config.Location)
                        .WithExistingResourceGroup(_config.ResourceGroup)
                        .WithTags(DefaultTags)
                        .WithSku(PublicIPSkuType.Standard)
                        .WithStaticIP()
                        .WithLeafDomainLabel(domainName)
                        .CreateAsync(cancellationToken));
                    continue;
                }

                unexpectedResources.Remove(existingIp);

                _logger.LogInformation("Updating existing IP address {ipName}", name);
                ips.Add(await (await azure.PublicIPAddresses.GetByResourceGroupAsync(_config.ResourceGroup, name, cancellationToken))
                    .Update()
                    .WithTags(DefaultTags)
                    .WithStaticIP()
                    .WithLeafDomainLabel(domainName)
                    .ApplyAsync(cancellationToken));
            }

            return ips;
        }

        private async Task<INetworkSecurityGroup> DeployNetworkSecurityGroup(List<IGenericResource> allResources, IAzure azure, CancellationToken cancellationToken)
        {
            string nsgName = _config.Name + "-nsg";

            IGenericResource nsg = allResources.FirstOrDefault(r =>
                r.ResourceProviderNamespace == "Microsoft.Network" &&
                r.ResourceType == "networkSecurityGroups" &&
                r.Name == nsgName);

            var neededRules = new[]
                {
                    ("AppGatewayRule", "65200-65535"),
                    ("ServiceFabricTcp", _config.TcpGatewayPort.ToString()),
                    ("ServiceFabricHttp", _config.HttpGatewayPort.ToString()),
                }.Concat(_config.NodeTypes
                    .SelectMany(n => n.Endpoints)
                    .Select(e => e.InternalPort)
                    .Distinct()
                    .Select((p, i) =>
                    ("SslEndpoint-" + i, p.ToString())))
                .ToDictionary(t => t.Item1, t => t.Item2);

            if (nsg == null)
            {
                _logger.LogInformation("Creating new network security group {nsgName}.", nsgName);
                IWithCreate nsgDef = azure.NetworkSecurityGroups.Define(nsgName)
                    .WithRegion(_config.Location)
                    .WithExistingResourceGroup(_config.ResourceGroup)
                    .WithTags(DefaultTags);

                var index = 1;
                foreach ((string key, string range) in neededRules)
                {
                    nsgDef = nsgDef.DefineRule(key)
                        .AllowInbound()
                        .FromAnyAddress()
                        .FromAnyPort()
                        .ToAnyAddress()
                        .ToPortRanges(range)
                        .WithProtocol(SecurityRuleProtocol.Tcp)
                        .WithPriority(1000 + index)
                        .Attach();

                    index++;
                }

                return await nsgDef.CreateAsync(cancellationToken);
            }

            allResources.Remove(nsg);

            _logger.LogInformation("Updating existing network security group {nsgName}.", nsg.Name);
            INetworkSecurityGroup existingGroup = await azure.NetworkSecurityGroups.GetByIdAsync(nsg.Id, cancellationToken);
            IEnumerable<string> existingRules = existingGroup.SecurityRules.Keys;

            IUpdate updatedNsg = existingGroup.Update();
            foreach (string rule in existingRules)
            {
                updatedNsg = updatedNsg.WithoutRule(rule);
            }

            {
                var index = 1;
                foreach ((string key, string range) in neededRules)
                {
                    updatedNsg = updatedNsg.DefineRule(key)
                        .AllowInbound()
                        .FromAnyAddress()
                        .FromAnyPort()
                        .ToAnyAddress()
                        .ToPortRanges(range)
                        .WithProtocol(SecurityRuleProtocol.Tcp)
                        .WithPriority(1000 + index)
                        .Attach();

                    index++;
                }
            }

            existingGroup = await updatedNsg.ApplyAsync(cancellationToken);

            return existingGroup;
        }

        private static void IgnoreResources(ICollection<IGenericResource> unexpectedResources, (string ns, string type)[] ignorables)
        {
            var toIgnore = unexpectedResources.Where(r =>
                ignorables.Any(t =>
                    string.Equals(r.ResourceProviderNamespace, t.ns, StringComparison.OrdinalIgnoreCase) &&
                    (string.Equals(r.ResourceType, t.type, StringComparison.OrdinalIgnoreCase) ||
                     t.type == "*")))
                .ToList();
            foreach (IGenericResource resource in toIgnore)
            {
                unexpectedResources.Remove(resource);
            }
        }

        private async Task<string> DeployApplicationInsights(ICollection<IGenericResource> allResources,
            IResourceManager resourceManager, CancellationToken cancellationToken)
        {
            IGenericResource ai = allResources.FirstOrDefault(r =>
                r.ResourceProviderNamespace == "Microsoft.Insights" &&
                r.ResourceType == "components" &&
                r.Name == _config.Name);
            
            _logger.LogInformation("Deploying application insights '{appInsightsName}'", _config.Name);

            if (ai != null)
            {
                allResources.Remove(ai);
            }

            JObject result = await DeployTemplateAsync("ApplicationInsights", resourceManager, new JObject
            {
                ["resources"] = new JArray
                {
                    new JObject
                    {
                        ["apiVersion"] = "2015-05-01",
                        ["type"] = "Microsoft.Insights/components",
                        ["name"] = _config.Name,
                        ["location"] = _config.Location,
                        ["kind"] = "web",
                        ["tags"] = JObject.FromObject(DefaultTags),
                        ["properties"] = new JObject
                        {
                            ["applicationId"] = _config.Name,
                        },
                    },
                },
                ["outputs"] = new JObject
                {
                    ["instrumentationKey"] = new JObject
                    {
                        ["value"] =
                            $"[reference(resourceId('Microsoft.Insights/components', '{_config.Name}'), '2015-05-01').InstrumentationKey]",
                        ["type"] = "string",
                    },
                },
            }, cancellationToken);

            return result.Value<JObject>("instrumentationKey").Value<string>("value");
        }

        private async Task<JObject> DeployTemplateAsync(string name, IResourceManager resourceManager, JObject template, CancellationToken cancellationToken)
        {
            template["$schema"] = "http://schema.management.azure.com/schemas/2015-01-01/deploymentTemplate.json";
            template["contentVersion"] = "1.0.0.0";

            _logger.LogInformation("Deploying template '{templateName}'", name);

            if (resourceManager.Deployments.CheckExistence(_config.ResourceGroup, name))
            {
                await resourceManager.Deployments.DeleteByResourceGroupAsync(_config.ResourceGroup, name, cancellationToken);
            }

            var deployment = await resourceManager.Deployments.Define(name)
                .WithExistingResourceGroup(_config.ResourceGroup)
                .WithTemplate(template)
                .WithParameters(new JObject())
                .WithMode(DeploymentMode.Incremental)
                .CreateAsync(cancellationToken);

            await deployment.RefreshAsync(cancellationToken);

            return (JObject) deployment.Outputs;
        }
    }
}
