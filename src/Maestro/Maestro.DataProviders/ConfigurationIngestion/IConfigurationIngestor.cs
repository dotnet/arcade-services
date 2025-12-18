// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Threading.Tasks;
using Maestro.DataProviders.ConfigurationIngestion.Helpers;
using Microsoft.DotNet.MaestroConfiguration.Client.Models;

#nullable enable
namespace Maestro.DataProviders.ConfigurationIngestion;

public interface IConfigurationIngestor
{
    /// <summary>
    ///  Ingests a configuration on a given namespace, validating and persisting its data.
    /// </summary>
    /// <returns>A record of the entity changes applied during ingestion.</returns>
    Task<ConfigurationUpdates> IngestConfigurationAsync(
        ConfigurationData configurationData,
        string configurationNamespace);
}

public record ConfigurationUpdates(
    EntityChanges<SubscriptionYaml> Subscriptions,
    EntityChanges<ChannelYaml> Channels,
    EntityChanges<DefaultChannelYaml> DefaultChannels,
    EntityChanges<BranchMergePoliciesYaml> RepositoryBranches);

public record ConfigurationData(
    IReadOnlyCollection<SubscriptionYaml> Subscriptions,
    IReadOnlyCollection<ChannelYaml> Channels,
    IReadOnlyCollection<DefaultChannelYaml> DefaultChannels,
    IReadOnlyCollection<BranchMergePoliciesYaml> BranchMergePolicies);
