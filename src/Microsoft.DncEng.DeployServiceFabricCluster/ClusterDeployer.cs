using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Azure.Storage;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Azure.Management.Fluent;
using Microsoft.Azure.Management.Network.Fluent;
using Microsoft.Azure.Management.Network.Fluent.LoadBalancer.Definition;
using Microsoft.Azure.Management.Network.Fluent.Models;
using Microsoft.Azure.Management.ResourceManager.Fluent;
using Microsoft.Azure.Management.Storage.Fluent;
using Microsoft.Azure.Management.Storage.Fluent.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Rest.Azure;
using Newtonsoft.Json.Linq;

namespace Microsoft.DncEng.DeployServiceFabricCluster
{
    internal class ClusterDeployer : ResourceGroupDeployer<string, ClusterSettings>
    {
        public ClusterDeployer(ClusterSettings settings, ILogger<ClusterDeployer> logger, IConfiguration config) : base(settings, logger, config)
        {
        }

        protected override void PopulateDefaultTags()
        {
            base.PopulateDefaultTags();
            DefaultTags["ClusterName"] = Settings.Name;
        }

        protected override async Task<string> DeployResourcesAsync(
            List<IGenericResource> unexpectedResources,
            IAzure azure,
            IResourceManager resourceManager,
            CancellationToken cancellationToken)
        {
            var (artifactStorageClient, artifactsLocationInfo) = await EnsureArtifactStorage(azure, cancellationToken);
            string artifactsLocation = artifactStorageClient.Uri.AbsoluteUri;

            string searchDir = Path.Join(AppContext.BaseDirectory, "scripts");
            List<string> fileNames = new List<string>();
            foreach (string file in Directory.EnumerateFiles(searchDir, "*", SearchOption.AllDirectories))
            {
                fileNames.Add(Path.GetRelativePath(searchDir, file));
                string relative = file.Substring(searchDir.Length + 1);
                await using var stream = new FileStream(file, FileMode.Open, FileAccess.Read);
                await artifactStorageClient.DeleteBlobIfExistsAsync(relative, cancellationToken: cancellationToken);
                await artifactStorageClient.UploadBlobAsync(relative, stream, cancellationToken);
            }

            IgnoreResources(
                unexpectedResources,
                new[]
                {
                    ("microsoft.alertsManagement", "*"),
                    ("Microsoft.Insights", "metricalerts"),
                    ("Microsoft.Insights", "webtests"),
                    ("Microsoft.Storage", "storageAccounts"),
                }
            );

            var (supportLogStorage, applicationDiagnosticsStorage) =
                await EnsureStorageAccounts(azure, cancellationToken);
            string instrumentationKey;
            if (Settings.ApplicationInsights != null)
            {
                instrumentationKey = await UseExistingApplicationInsights(
                    resourceManager,
                    Settings.ApplicationInsights,
                    cancellationToken
                );
            }
            else
            {
                instrumentationKey = await DeployApplicationInsights(
                    unexpectedResources,
                    resourceManager,
                    cancellationToken
                );
            }

            var clusterIp = await DeployPublicIp(
                unexpectedResources,
                azure,
                Settings.Name + "-IP",
                Settings.Name,
                cancellationToken
            );

            string clusterEndpoint = await DeployServiceFabric(
                unexpectedResources,
                resourceManager,
                clusterIp,
                supportLogStorage,
                cancellationToken
            );

            int idx = 1;
            foreach (var nodeType in Settings.NodeTypes)
            {
                var backendAddressPool = await EnsureBackendAddressPool(azure, nodeType, cancellationToken);
                var subnet = await GetSubnet(azure, idx, cancellationToken);

                if (nodeType.Name == "Primary")
                {
                    ILoadBalancer lb = await DeployLoadBalancer(
                        unexpectedResources,
                        azure,
                        nodeType,
                        clusterIp,
                        cancellationToken
                    );
                    await DeployScaleSet(
                        unexpectedResources,
                        resourceManager,
                        nodeType,
                        lb,
                        backendAddressPool,
                        subnet,
                        clusterEndpoint,
                        instrumentationKey,
                        artifactsLocation,
                        fileNames,
                        artifactsLocationInfo,
                        supportLogStorage,
                        applicationDiagnosticsStorage,
                        cancellationToken
                    );
                }
                else
                {
                    await DeployScaleSet(
                        unexpectedResources,
                        resourceManager,
                        nodeType,
                        null,
                        backendAddressPool,
                        subnet,
                        clusterEndpoint,
                        instrumentationKey,
                        artifactsLocation,
                        fileNames,
                        artifactsLocationInfo,
                        supportLogStorage,
                        applicationDiagnosticsStorage,
                        cancellationToken
                    );
                }

                idx++;
            }

            return "";
        }

        private async Task DeployScaleSet(
            ICollection<IGenericResource> unexpectedResources,
            IResourceManager resourceManager,
            ServiceFabricNodeType nodeType,
            ILoadBalancer? lb,
            string backendAddressPool,
            ISubnet subnet,
            string clusterEndpoint,
            string instrumentationKey,
            string artifactsLocation,
            List<string> fileNames,
            StorageAccountInfo artifactsLocationInfo,
            StorageAccountInfo supportLogStorage,
            StorageAccountInfo applicationDiagnosticsStorage,
            CancellationToken cancellationToken)
        {
            string scaleSetName = $"{Settings.Name}-{nodeType.Name}";

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
                        ["location"] = Settings.Location,
                        ["identity"] = nodeType.UserAssignedIdentity == null ? new JObject
                        {
                            ["type"] =  "SystemAssigned",
                        } : new JObject
                        {
                            ["type"] = "userAssigned",
                            ["userAssignedIdentities"] = new JObject
                            {
                                [GetResourceId(nodeType.UserAssignedIdentity, "Microsoft.ManagedIdentity/userAssignedIdentities")] = new JObject{},
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
                                                            Settings.SslCertificateCommonName,
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
                                                    ["fileUris"] = new JArray(fileNames.Select(file => artifactsLocation + "/" + file.Replace('\\', '/'))),
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
                                                            ["loadBalancerBackendAddressPools"] = lb != null ? new JArray
                                                            {
                                                                new JObject
                                                                {
                                                                    ["id"] = lb.Backends.First().Value.Inner.Id,
                                                                },
                                                            } : null,
                                                            ["applicationGatewayBackendAddressPools"] = new JArray
                                                            {
                                                                new JObject
                                                                {
                                                                    ["id"] = backendAddressPool,
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
                                    ["adminUsername"] = Settings.AdminUsername,
                                    ["adminPassword"] = Settings.AdminPassword,
                                    ["computernamePrefix"] = nodeType.Name,
                                    ["secrets"] = new JArray
                                    {
                                        new JObject
                                        {
                                            ["sourceVault"] = new JObject
                                            {
                                                ["id"] = GetCertificateSourceVaultId(),
                                            },
                                            ["vaultCertificates"] = new JArray(
                                                Settings.Certificates.SelectMany(GetKeyVaultSecretIds).Select(certUrl => new JObject
                                                {
                                                    ["certificateUrl"] = certUrl,
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

        private string GetCertificateSourceVaultId()
        {
            return GetResourceId(Settings.CertificateSourceVault, "Microsoft.KeyVault/vaults");
        }

        private async Task<ILoadBalancer> DeployLoadBalancer(ICollection<IGenericResource> unexpectedResources, IAzure azure, ServiceFabricNodeType nodeType, IPublicIPAddress publicIp, CancellationToken cancellationToken)
        {
            string lbName = $"{Settings.Name}-{nodeType.Name}-LB";

            IGenericResource existingLoadBalancer = unexpectedResources.FirstOrDefault(r =>
                string.Equals(r.ResourceProviderNamespace, "Microsoft.Network", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(r.ResourceType, "loadBalancers", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(r.Name, lbName, StringComparison.OrdinalIgnoreCase));

            Debug.Assert(nodeType.Name == "Primary");

            var neededProbesAndRules = new List<(string name, int externalPort, int internalPort)>();

            neededProbesAndRules.AddRange(new[]
            {
                ("FabricTcpGateway", ServiceFabricConstants.TcpGatewayPort, ServiceFabricConstants.TcpGatewayPort),
                ("FabricHttpGateway", ServiceFabricConstants.HttpGatewayPort, ServiceFabricConstants.HttpGatewayPort),
            });

            if (existingLoadBalancer == null)
            {
                Logger.LogInformation("Creating new load balancer {lbName}", lbName);

                var def = azure.LoadBalancers.Define(lbName)
                    .WithRegion(Settings.Location)
                    .WithExistingResourceGroup(Settings.ResourceGroup);

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

                return await ((IWithLBRuleOrNatOrCreate)def)
                    .WithTags(DefaultTags)
                    .WithSku(LoadBalancerSkuType.Standard)
                    .CreateAsync(cancellationToken);
            }

            Logger.LogInformation("Updating existing load balancer {lbName}", lbName);

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

        private async Task<ISubnet> GetSubnet(IAzure azure, int index, CancellationToken cancellationToken)
        {
            var network = await azure.Networks.GetByIdAsync(GetResourceId(Settings.VNet, "Microsoft.Network/virtualNetworks"), cancellationToken);
            var subnetName = $"Cluster-{Settings.ClusterIndex}-Node-{index}";
            if (!network.Subnets.TryGetValue(subnetName, out var subnet))
            {
                throw new InvalidOperationException($"Subnet {subnetName} not found.");
            }

            return subnet;
        }

        private async Task<string> EnsureBackendAddressPool(IAzure azure, ServiceFabricNodeType type, CancellationToken cancellationToken)
        {
            var backendName = $"{Settings.Name}-{type.Name}";
            return await EnsureBackendAddressPool(azure, backendName, type.Ports, cancellationToken);
        }

        private async Task<string> EnsureBackendAddressPool(IAzure azure, string backendName, List<int> ports, CancellationToken cancellationToken)
        {
            var gatewayId = GetResourceId(Settings.Gateway, "Microsoft.Network/applicationGateways");
            var gateway = await azure.ApplicationGateways.GetByIdAsync(gatewayId, cancellationToken);
            var backendId = $"{gatewayId}/backendAddressPools/{backendName}";
            if (!gateway.Backends.ContainsKey(backendName))
            {
                var update = gateway.Update()
                    .DefineBackend(backendName)
                    .Attach();
                foreach (var port in ports)
                {
                    update = update
                        .DefineBackendHttpConfiguration($"{backendName}-{port}")
                        .WithProtocol(ApplicationGatewayProtocol.Http)
                        .WithPort(port)
                        .WithCookieBasedAffinity()
                        .WithRequestTimeout(20)
                        .Attach();
                }

                await update.ApplyAsync(cancellationToken);
            }

            return backendId;
        }

        private async Task<string> DeployServiceFabric(ICollection<IGenericResource> unexpectedResources,
            IResourceManager resourceManager, IPublicIPAddress primaryIp, StorageAccountInfo supportLogStorage,
            CancellationToken cancellationToken)
        {
            IGenericResource svcFabResource = unexpectedResources.FirstOrDefault(r =>
                r.ResourceProviderNamespace == "Microsoft.ServiceFabric" &&
                r.ResourceType == "clusters" &&
                r.Name == Settings.Name);

            if (svcFabResource != null)
            {
                unexpectedResources.Remove(svcFabResource);

                while (true)
                {
                    IGenericResource svcFab = await resourceManager.GenericResources.GetByIdAsync(svcFabResource.Id, cancellationToken: cancellationToken);
                    var props = (JObject)svcFab.Properties;
                    string state = props["clusterState"]?.Value<string>() ?? "Ready";

                    if (state == "Ready" ||
                        state == "WaitingForNodes")
                    {
                        break;
                    }

                    Logger.LogInformation("Service Fabric Resource is in state '{state}', cannot deploy yet.", state);
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
                            ["name"] = Settings.Name,
                            ["location"] = Settings.Location,
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
                                            ["certificateCommonName"] = Settings.SslCertificateCommonName,
                                            ["certificateIssuerThumbprint"] = "",
                                        },
                                    },
                                    ["x509StoreName"] = "My",
                                },
                                ["clientCertificateCommonNames"] = new JArray
                                {
                                    new JObject
                                    {
                                        ["certificateCommonName"] = Settings.AdminClientCertificateCommonName,
                                        ["certificateIssuerThumbprint"] = Settings.AdminClientCertificateIssuerThumbprint,
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
                                ["managementEndpoint"] = $"https://{primaryIp.Fqdn}:{ServiceFabricConstants.HttpGatewayPort}",
                                ["reliabilityLevel"] = "Silver",
                                ["upgradeMode"] = "Automatic",
                                ["vmImage"] = "Windows",
                                ["nodeTypes"] = new JArray(
                                    Settings.NodeTypes.Select(nt => new JObject
                                    {
                                        ["name"] = nt.Name,
                                        ["applicationPorts"] = new JObject
                                        {
                                            ["startPort"] = 20000,
                                            ["endPort"] = 30000,
                                        },
                                        ["clientConnectionEndpointPort"] = ServiceFabricConstants.TcpGatewayPort,
                                        ["durabilityLevel"] = "Silver",
                                        ["ephemeralPorts"] = new JObject
                                        {
                                            ["startPort"] = 49152,
                                            ["endPort"] = 65534,
                                        },
                                        ["httpGatewayEndpointPort"] = ServiceFabricConstants.HttpGatewayPort,
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
                            ["value"] = $"[reference(resourceId('Microsoft.ServiceFabric/clusters', '{Settings.Name}')).clusterEndpoint]",
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

                    string endpoint = ((JObject)svcFab.Properties).Value<string>("clusterEndpoint");
                    if (!string.IsNullOrEmpty(endpoint))
                    {
                        Logger.LogWarning(ex, "Error deploying Service Fabric: {message}", ex.Message);
                        Logger.LogInformation("Service Fabric exists and has a cluster endpoint, proceeding to VM deployment");
                        return endpoint;
                    }
                }

                throw;
            }
        }

        private async Task<string> DeployApplicationInsights(ICollection<IGenericResource> allResources,
            IResourceManager resourceManager, CancellationToken cancellationToken)
        {
            IGenericResource ai = allResources.FirstOrDefault(r =>
                r.ResourceProviderNamespace == "Microsoft.Insights" &&
                r.ResourceType == "components" &&
                r.Name == Settings.Name);

            Logger.LogInformation("Deploying application insights '{appInsightsName}'", Settings.Name);

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
                        ["name"] = Settings.Name,
                        ["location"] = Settings.Location,
                        ["kind"] = "web",
                        ["tags"] = JObject.FromObject(DefaultTags),
                        ["properties"] = new JObject
                        {
                            ["applicationId"] = Settings.Name,
                        },
                    },
                },
                ["outputs"] = new JObject
                {
                    ["instrumentationKey"] = new JObject
                    {
                        ["value"] =
                            $"[reference(resourceId('Microsoft.Insights/components', '{Settings.Name}'), '2015-05-01').InstrumentationKey]",
                        ["type"] = "string",
                    },
                },
            }, cancellationToken);

            return result.Value<JObject>("instrumentationKey").Value<string>("value");
        }

        private async Task<string> UseExistingApplicationInsights(IResourceManager resourceManager, ResourceReference aiResource, CancellationToken cancellationToken)
        {
            var id = GetResourceId(aiResource, "Microsoft.Insights/components");
            var resource = await resourceManager.GenericResources.GetByIdAsync(id, cancellationToken: cancellationToken);
            return ((JObject) resource.Properties).Value<string>("InstrumentationKey");
        }

        private async Task<(StorageAccountInfo supportLogStorage, StorageAccountInfo applicationDiagnosticsStorage)> EnsureStorageAccounts(IAzure azure, CancellationToken cancellationToken)
        {
            StorageAccountInfo[] accounts = await Task.WhenAll(
                EnsureStorageAccount(azure, Settings.ResourceGroup, "sflogs" + UniqueString(18), cancellationToken),
                EnsureStorageAccount(azure, Settings.ResourceGroup, "sfdg" + UniqueString(20), cancellationToken)
            );
            return (accounts[0], accounts[1]);
        }

        private string UniqueString(int length)
        {
            using var ms = new MemoryStream();
            using var writer = new StreamWriter(ms);
            writer.Write(Settings.SubscriptionId.ToString());
            writer.Write(Settings.ResourceGroup);
            ms.Position = 0;
            using var hasher = SHA256.Create();
            var bytes = hasher.ComputeHash(ms);
            return BitConverter.ToString(bytes).Replace("-", "").Substring(0, length).ToLowerInvariant();
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

        private async Task<(BlobContainerClient container, StorageAccountInfo account)> EnsureArtifactStorage(IAzure azure, CancellationToken cancellationToken)
        {
            string storageAccountName = "stage" + Settings.SubscriptionId.ToString("N").Substring(0, 19);
            StorageAccountInfo accountInfo = await EnsureStorageAccount(azure, "ARM_Deploy_Staging", storageAccountName, cancellationToken);

            BlobContainerClient container = accountInfo.Client.GetBlobContainerClient(Settings.Name + "-stageartifacts");
            await container.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: cancellationToken);

            return (container, accountInfo);
        }

        private async Task<StorageAccountInfo> EnsureStorageAccount(IAzure azure, string resourceGroupName, string storageAccountName, CancellationToken cancellationToken)
        {
            Logger.LogInformation("Ensuring storage account '{accountName}' exists.", storageAccountName);

            var storageAccount = (await azure.StorageAccounts.ListAsync(true, cancellationToken)).FirstOrDefault(s => s.Name == storageAccountName);
            if (storageAccount == null)
            {
                IResourceGroup resourceGroup = await azure.ResourceGroups.GetByNameAsync(resourceGroupName, cancellationToken);
                if (resourceGroup == null)
                {
                    resourceGroup = await azure.ResourceGroups.Define(resourceGroupName)
                        .WithRegion(Settings.Location)
                        .CreateAsync(cancellationToken);
                }

                storageAccount = await azure.StorageAccounts.Define(storageAccountName)
                    .WithRegion(Settings.Location)
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
    }
}
