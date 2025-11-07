// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text;
using Microsoft.DotNet.Darc.Helpers;
using Microsoft.DotNet.DarcLib.Models.Darc;

namespace ProductConstructionService.ScenarioTests.Helpers;

public class DependencyCollectionStringBuilder
{
    internal static string GetString(List<DependencyDetail> expectedDependencies)
    {
        var stringBuilder = new StringBuilder();

        foreach (DependencyDetail dependency in expectedDependencies)
        {
            stringBuilder.AppendLine(UxHelpers.DependencyToString(dependency));
        }

        return stringBuilder.ToString();
    }
}
