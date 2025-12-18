// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.DotNet.MaestroConfiguration.Client.Models;

#nullable enable
namespace Maestro.DataProviders.ConfigurationIngestion.Model;

internal class IngestedBranchMergePolicies : IExternallySyncedEntity<(string Repository, string Branch)>
{
    public IngestedBranchMergePolicies(BranchMergePoliciesYaml values) => Values = values;

    public (string, string) UniqueId => (Values.Repository, Values.Branch);

    public BranchMergePoliciesYaml Values { init; get; }
}
