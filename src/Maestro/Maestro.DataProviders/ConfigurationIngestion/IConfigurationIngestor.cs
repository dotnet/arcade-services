// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Threading.Tasks;
using Maestro.DataProviders.ConfigurationIngestion.Model;
using Microsoft.DotNet.MaestroConfiguration.Client.Models;

#nullable enable
namespace Maestro.DataProviders.ConfigurationIngestion;

public interface IConfigurationIngestor
{
    /// <summary>
    ///  Ingests a configuration on a given namespace, validating and persisting its data.
    /// </summary>
    /// <param name="configurationData">The configuration data to ingest.</param>
    /// <param name="configurationNamespace">The namespace under which to ingest the configuration.</param>
    /// <param name="saveChanges">Whether to save changes to the database after ingestion.</param>
    /// <param name="createRepositoryIds">Whether to create repository registrations in the DB.</param>
    /// <returns>A record of the entity changes applied during ingestion.</returns>
    Task<ConfigurationUpdates> IngestConfigurationAsync(
        ConfigurationData configurationData,
        string configurationNamespace,
        bool saveChanges = true,
        bool createRepositoryIds = true);
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
