using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.Management.Fluent;
using Microsoft.Azure.Management.Network.Fluent;
using Microsoft.Azure.Management.Network.Fluent.Models;
using Microsoft.Azure.Management.ResourceManager.Fluent;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace Microsoft.DncEng.DeployServiceFabricCluster
{
    internal class GatewayDeployer : ResourceGroupDeployer<string, GatewaySettings>
    {
        public GatewayDeployer(GatewaySettings settings, ILogger<GatewayDeployer> logger, IConfiguration config) : base(settings, logger, config)
        {
        }

        protected override async Task<string> DeployResourcesAsync(List<IGenericResource> unexpectedResources, IAzure azure, IResourceManager resourceManager, CancellationToken cancellationToken)
        {
            var gatewayIp = await DeployPublicIp(unexpectedResources, azure, Settings.Name + "-IP", Settings.Name, cancellationToken);
            INetworkSecurityGroup nsg = await DeployNetworkSecurityGroup(unexpectedResources, gatewayIp, azure, cancellationToken);

            INetwork vnet = await DeployVirtualNetwork(unexpectedResources, azure, nsg, cancellationToken);

            var gatewayId = await DeployApplicationGateway(unexpectedResources, resourceManager, gatewayIp, vnet, cancellationToken);

            return $"VNet='{vnet.Id}'\nGateway='{gatewayId}'\n";
        }

        private async Task<INetworkSecurityGroup> DeployNetworkSecurityGroup(List<IGenericResource> allResources, IPublicIPAddress gatewayIp, IAzure azure, CancellationToken cancellationToken)
        {
            string nsgName = Settings.Name + "-nsg";

            IGenericResource nsg = allResources.FirstOrDefault(r =>
                r.ResourceProviderNamespace == "Microsoft.Network" &&
                r.ResourceType == "networkSecurityGroups" &&
                r.Name == nsgName);

            var neededRules = Settings.NeededSecurityGroupRules;

            if (nsg == null)
            {
                Logger.LogInformation("Creating new network security group {nsgName}.", nsgName);
                var nsgDef = azure.NetworkSecurityGroups.Define(nsgName)
                    .WithRegion(Settings.Location)
                    .WithExistingResourceGroup(Settings.ResourceGroup)
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

            Logger.LogInformation("Updating existing network security group {nsgName}.", nsg.Name);
            INetworkSecurityGroup existingGroup = await azure.NetworkSecurityGroups.GetByIdAsync(nsg.Id, cancellationToken);
            IEnumerable<string> existingRules = existingGroup.SecurityRules.Keys
                .Where(ruleName => !ruleName.StartsWith("NRMS")); // Ignore rules created by the microsoft management stuff

            var updatedNsg = existingGroup.Update();
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

        private async Task<INetwork> DeployVirtualNetwork(ICollection<IGenericResource> unexpectedResources,
            IAzure azure, INetworkSecurityGroup nsg, CancellationToken cancellationToken)
        {
            string vnetName = Settings.Name + "-vnet";

            IGenericResource existingNetworkResource = unexpectedResources.FirstOrDefault(r =>
                string.Equals(r.ResourceProviderNamespace, "Microsoft.Network", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(r.ResourceType, "virtualNetworks", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(r.Name, vnetName, StringComparison.OrdinalIgnoreCase));

            var pairs = Enumerable.Range(1, 5).SelectMany(i => Enumerable.Range(1, 5).Select(j => (i, j)));
            var nodeSubnets = pairs.Select(p => ($"Cluster-{p.i}-Node-{p.j}", $"10.{p.i}.{p.j}.0/24"));
            IEnumerable<(string, string)> neededSubnets = new[]
            {
                ("AppGateway", "10.0.0.0/24"),
            }.Concat(nodeSubnets);

            if (existingNetworkResource == null)
            {
                Logger.LogInformation("Creating new virtual network {vnetName}", vnetName);

                var networkDef = azure.Networks.Define(vnetName)
                    .WithRegion(Settings.Location)
                    .WithExistingResourceGroup(Settings.ResourceGroup)
                    .WithAddressSpace("10.0.0.0/8");
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

            Logger.LogInformation("Updating existing virtual network {vnetName}", vnetName);

            unexpectedResources.Remove(existingNetworkResource);

            INetwork existingNetwork = await azure.Networks.GetByIdAsync(existingNetworkResource.Id, cancellationToken);

            var update = existingNetwork.Update();
            foreach (string space in existingNetwork.AddressSpaces)
            {
                update = update.WithoutAddressSpace(space);
            }
            foreach (KeyValuePair<string, ISubnet> subnet in existingNetwork.Subnets)
            {
                update = update.WithoutSubnet(subnet.Key);
            }

            update = update.WithAddressSpace("10.0.0.0/8");
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

        private async Task<string> DeployApplicationGateway(List<IGenericResource> unexpectedResources, IResourceManager resourceManager, IPublicIPAddress gatewayIp, INetwork vnet, CancellationToken cancellationToken)
        {
            var gatewayName = Settings.Name;

            var existingGatewayResource = unexpectedResources.FirstOrDefault(r =>
                string.Equals(r.ResourceProviderNamespace, "Microsoft.Network", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(r.ResourceType, "applicationGateways", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(r.Name, gatewayName, StringComparison.OrdinalIgnoreCase));

            if (existingGatewayResource != null)
            {
                unexpectedResources.Remove(existingGatewayResource);
            }

            var gatewaySubnet = vnet.Subnets.First(s =>
                string.Equals(s.Key, "AppGateway", StringComparison.OrdinalIgnoreCase)).Value;
            var gatewaySubnetRef = vnet.Id + "/subnets/" + gatewaySubnet.Name;

            var gatewayId = GetResourceId(new ResourceReference { Name = gatewayName }, "Microsoft.Network/applicationGateways");

            await DeployTemplateAsync("Gateway-" + Settings.Name, resourceManager, new JObject
            {
                ["resources"] = new JArray
                {
                    new JObject
                    {
                        ["name"] = gatewayName,
                        ["type"] = "Microsoft.Network/applicationGateways",
                        ["apiVersion"] = "2019-09-01",
                        ["location"] = Settings.Location,
                        ["zones"] = new JArray(),
                        ["tags"] = JObject.FromObject(DefaultTags),
                        ["identity"] = new JObject
                        {
                            ["type"] = "UserAssigned",
                            ["userAssignedIdentities"] = new JObject
                            {
                                [GetResourceId(Settings.UserAssignedIdentity, "Microsoft.ManagedIdentity/userAssignedIdentities")] = new JObject{},
                            },
                        },
                        ["properties"] = new JObject
                        {
                            ["sku"] = new JObject
                            {
                                ["name"] = "Standard_v2",
                                ["tier"] = "Standard_v2",
                            },
                            ["gatewayIPConfigurations"] = new JArray
                            {
                                new JObject
                                {
                                    ["name"] = "appGatewayIpConfig",
                                    ["properties"] = new JObject
                                    {
                                        ["subnet"] = new JObject
                                        {
                                            ["id"] = gatewaySubnetRef,
                                        },
                                    },
                                },
                            },
                            ["frontendIPConfigurations"] = new JArray
                            {
                                new JObject
                                {
                                    ["name"] = "frontendIPConfig",
                                    ["properties"] = new JObject
                                    {
                                        ["PublicIPAddress"] = new JObject
                                        {
                                            ["id"] = gatewayIp.Id,
                                        },
                                    },
                                },
                            },
                            ["frontendPorts"] = new JArray(
                                Settings.ExternalPorts
                                    .Select(port => new JObject
                                    {
                                        ["name"] = $"Frontend-{port}",
                                        ["properties"] = new JObject
                                        {
                                            ["Port"] = port,
                                        },
                                    })),
                            ["backendAddressPools"] = new JArray(new JObject
                            {
                                ["name"] = "Nothing",
                                ["properties"] = new JObject { },
                            }),
                            ["backendHttpSettingsCollection"] = new JArray(new JObject
                            {
                                ["name"] = "default-http-settings",
                                ["properties"] = new JObject
                                {
                                    ["Port"] = 8080,
                                    ["Protocol"] = "Http",
                                    ["cookieBasedAffinity"] = "Enabled",
                                    ["requestTimeout"] = 20,
                                },
                            }),
                            ["httpListeners"] = new JArray(
                                Settings.ExternalPorts
                                    .Select(port => new JObject
                                    {
                                        ["name"] = $"{port}-listener",
                                        ["properties"] = new JObject
                                        {
                                            ["frontendIPConfiguration"] = new JObject
                                            {
                                                ["id"] = $"{gatewayId}/frontendIPConfigurations/frontendIPConfig",
                                            },
                                            ["frontendPort"] = new JObject
                                            {
                                                ["id"] = $"{gatewayId}/frontendPorts/Frontend-{port}",
                                            },
                                            ["protocol"] = "Https",
                                            ["sslCertificate"] = new JObject
                                            {
                                                ["id"] = $"{gatewayId}/sslCertificates/frontendSslCertificate",
                                            }
                                        },
                                    })),
                            ["requestRoutingRules"] = new JArray(
                                Settings.ExternalPorts
                                    .Select(port => new JObject
                                    {
                                        ["Name"] = $"RoutingRule-{port}",
                                        ["properties"] = new JObject
                                        {
                                            ["RuleType"] = "Basic",
                                            ["httpListener"] =  new JObject
                                            {
                                                ["id"] = $"{gatewayId}/httpListeners/{port}-listener",
                                            },
                                            ["backendAddressPool"] = new JObject
                                            {
                                                ["id"] = $"{gatewayId}/backendAddressPools/Nothing",
                                            },
                                            ["backendHttpSettings"] = new JObject
                                            {
                                                ["id"] = $"{gatewayId}/backendHttpSettingsCollection/default-http-settings",
                                            },
                                        },
                                    })),
                            ["enableHttp2"] = false,
                            ["sslCertificates"] = new JArray
                            {
                                new JObject
                                {
                                    ["name"] = "frontendSslCertificate",
                                    ["properties"] = new JObject
                                    {
                                        ["keyVaultSecretId"] = GetKeyVaultSecretId(Settings.SslCertificateName),
                                    },
                                },
                            },
                            ["probes"] = new JArray(),
                            ["autoscaleConfiguration"] = new JObject
                            {
                                ["minCapacity"] = 1,
                                ["maxCapacity"] = 10,
                            },
                        },
                    },
                },
            }, cancellationToken);

            return gatewayId;
        }
    }
}
