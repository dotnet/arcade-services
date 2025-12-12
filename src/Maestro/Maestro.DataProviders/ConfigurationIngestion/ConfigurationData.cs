// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.DotNet.DarcLib.Models.Yaml;
using System.Collections.Generic;

#nullable enable
namespace Maestro.DataProviders.ConfigurationIngestion;

public record ConfigurationData(
    IEnumerable<SubscriptionYaml> Subscriptions,
    IEnumerable<ChannelYaml> Channels,
    IEnumerable<DefaultChannelYaml> DefaultChannels,
    IEnumerable<BranchMergePoliciesYaml> BranchMergePolicies);
