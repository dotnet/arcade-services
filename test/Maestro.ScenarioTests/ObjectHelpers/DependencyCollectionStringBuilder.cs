// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Text;
using Microsoft.DotNet.Darc;
using Microsoft.DotNet.DarcLib;

namespace Maestro.ScenarioTests.ObjectHelpers
{
    public class DependencyCollectionStringBuilder
    {
        internal static string GetString(List<DependencyDetail> expectedDependencies)
        {
            StringBuilder stringBuilder = new StringBuilder();

            foreach(DependencyDetail dependency in expectedDependencies)
            {
                stringBuilder.AppendLine(UxHelpers.DependencyToString(dependency));
            }

            return stringBuilder.ToString();
        }
    }
}
