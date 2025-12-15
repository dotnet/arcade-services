// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Maestro.DataProviders.ConfigurationIngestion.Helpers;
using Microsoft.DotNet.DarcLib.Models.Yaml;
using System.Collections.Generic;

#nullable enable
namespace Maestro.DataProviders.ConfigurationIngestion;

public record ConfigurationData(
    IReadOnlyCollection<IngestedSubscription> Subscriptions,
    IReadOnlyCollection<IngestedChannel> Channels,
    IReadOnlyCollection<IngestedDefaultChannel> DefaultChannels,
    IReadOnlyCollection<IngestedBranchMergePolicies> BranchMergePolicies);
