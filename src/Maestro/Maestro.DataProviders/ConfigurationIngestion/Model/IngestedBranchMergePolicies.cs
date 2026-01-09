// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.DotNet.MaestroConfiguration.Client.Models;

#nullable enable
namespace Maestro.DataProviders.ConfigurationIngestion.Model;

internal class IngestedBranchMergePolicies : IExternallySyncedEntity<(string Repository, string Branch)>
{
    public IngestedBranchMergePolicies(BranchMergePoliciesYaml values) => _values = values;

    public override (string, string) UniqueId => (_values.Repository, _values.Branch);

    public BranchMergePoliciesYaml _values { init; get; }

    public override string SerializedData => _yamlSerializer.Serialize(_values);

    public override string ToString()
    {
        return $"BranchMergePolicies (Repository: '{_values.Repository}', Branch: '{_values.Branch}')";
    }
}
