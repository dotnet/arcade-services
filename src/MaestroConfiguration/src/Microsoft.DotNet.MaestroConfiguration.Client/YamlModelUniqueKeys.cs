// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using Microsoft.DotNet.MaestroConfiguration.Client.Models;

namespace Microsoft.DotNet.MaestroConfiguration.Client;

public static class YamlModelUniqueKeys
{
    public static Guid GetSubscriptionKey(SubscriptionYaml model)
        => model.Id;
    public static (string, string, string, string, bool, string?, string?) GetSubscriptionEquivalencyKey(SubscriptionYaml model)
        => (model.SourceRepository, model.Channel, model.TargetRepository, model.TargetBranch, model.SourceEnabled, model.SourceDirectory, model.TargetDirectory);
    public static (string Repository, string Branch, string Channel) GetDefaultChannelKey(DefaultChannelYaml model)
        => (model.Repository, model.Branch, model.Channel);
    public static string GetChannelKey(ChannelYaml model)
        => model.Name;
    public static (string Repository, string Branch) GetBranchMergePoliciesKey(BranchMergePoliciesYaml model)
        => (model.Repository, model.Branch);
}
