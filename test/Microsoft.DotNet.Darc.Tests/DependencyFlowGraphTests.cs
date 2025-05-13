// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using NUnit.Framework;

namespace Microsoft.DotNet.Darc.Tests;

[TestFixture]
public class DependencyFlowGraphTests
{
    [TestCase("IncludeBuildTimes", ".NET Core 5 Dev", true, new string[] {"everyMonth", "everyTwoWeeks", "everyWeek", "twiceDaily", "everyDay", "everyBuild", "none",}, false)]
    public void ValidateGraph(string testName, string channelName, bool includeBuildTimes, IEnumerable<string> includedFrequencies, bool includeDisabledSubscriptions)
    {
        DependencyFlowTestDriver.GetGraphAndCompare(testName, driver =>
        {
            return driver.GetDependencyFlowGraph(channelName, includeBuildTimes, includedFrequencies, includeDisabledSubscriptions);
        });
    }
}
