// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using Microsoft.DotNet.DarcLib.Models.Yaml;

namespace Maestro.DataProviders.ConfigurationIngestion;

public record ConfigurationDataUpdate(
    EntityChanges<SubscriptionYaml> Subscriptions,
    EntityChanges<ChannelYaml> Channels,
    EntityChanges<DefaultChannelYaml> DefaultChannels,
    EntityChanges<BranchMergePoliciesYaml> RepositoryBranches);

public record EntityChanges<T>(
    IEnumerable<T> Creations,
    IEnumerable<T> Updates,
    IEnumerable<T> Removals) where T: class;
