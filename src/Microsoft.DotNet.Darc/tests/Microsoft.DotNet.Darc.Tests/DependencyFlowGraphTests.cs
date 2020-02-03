// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.DotNet.DarcLib;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Xml;
using Xunit;

namespace Microsoft.DotNet.Darc.Tests
{
    public class DependencyFlowGraphTests
    {
        [Theory]
        [InlineData("IncludeBuildTimes", ".NET Core 5 Dev", true, new string[] {"everyWeek", "twiceDaily", "everyDay", "everyBuild", "none",}, false)]
        public void ValidateGraph(string testName, string channelName, bool includeBuildTimes, IEnumerable<string> includedFrequencies, bool includeDisabledSubscriptions)
        {
            DependencyFlowTestDriver.GetGraphAndCompare(testName, driver =>
            {
                return driver.GetDependencyFlowGraph(channelName, includeBuildTimes, includedFrequencies, includeDisabledSubscriptions);
            });
        }
    }
}
