// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.DotNet.MaestroConfiguration.Client.Models;

namespace Microsoft.DotNet.MaestroConfiguration.Client;

internal class YamlModelFunctionalKey
{
    internal static (string, string, string, string, bool, string?, string?) GetSubscriptionKey(SubscriptionYaml model)
        => (model.SourceRepository, model.Channel, model.TargetRepository, model.TargetBranch, model.SourceEnabled, model.SourceDirectory, model.TargetDirectory);
    internal static (string Repository, string Branch, string Channel) GetDefaultChannelKey(DefaultChannelYaml model)
        => YamlModelUniquenessKeys.GetDefaultChannelKey(model);
    internal static string GetChannelKey(ChannelYaml model)
        => YamlModelUniquenessKeys.GetChannelKey(model);
    internal static (string Repository, string Branch) GetBranchMergePoliciesKey(BranchMergePoliciesYaml model)
        => YamlModelUniquenessKeys.GetBranchMergePoliciesKey(model);
}
