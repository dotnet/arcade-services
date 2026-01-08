// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.DotNet.MaestroConfiguration.Client.Models;

#nullable enable
namespace Maestro.DataProviders.ConfigurationIngestion.Model;

internal class IngestedBranchMergePolicies : IExternallySyncedEntity<(string Repository, string Branch)>
{
    public IngestedBranchMergePolicies(BranchMergePoliciesYaml values) => Values = values;

    public override (string, string) UniqueId => (Values.Repository, Values.Branch);

    public BranchMergePoliciesYaml Values { init; get; }

    public override string ToString()
    {
        return $"BranchMergePolicies (Repository: '{Values.Repository}', Branch: '{Values.Branch}')";
    }
}
