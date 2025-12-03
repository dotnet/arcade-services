// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Maestro.Data.Models;
using System.Collections.Generic;

namespace Maestro.DataProviders.ConfigurationIngestor;

internal record ConfigurationData(
    IEnumerable<Subscription> Subscriptions,
    IEnumerable<Channel> Channels,
    IEnumerable<DefaultChannel> DefaultChannels,
    IEnumerable<RepositoryBranch> BranchMergePolicies);
