// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Maestro.Data.Models;
using System.Collections.Generic;

#nullable enable
namespace Maestro.DataProviders.ConfigurationIngestor;

public record ConfigurationData(
    IEnumerable<Subscription> Subscriptions,
    IEnumerable<Channel> Channels,
    IEnumerable<DefaultChannel> DefaultChannels,
    IEnumerable<RepositoryBranch> BranchMergePolicies);
