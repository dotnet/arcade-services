// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Xml;
using FluentAssertions;
using Microsoft.DotNet.DarcLib;
using NUnit.Framework;

namespace Microsoft.DotNet.Darc.Tests
{
    [TestFixture]
    public class DependencyFlowGraphTests
    {
        [TestCase("IncludeBuildTimes", ".NET Core 5 Dev", true, new string[] {"everyWeek", "twiceDaily", "everyDay", "everyBuild", "none",}, false)]
        public void ValidateGraph(string testName, string channelName, bool includeBuildTimes, IEnumerable<string> includedFrequencies, bool includeDisabledSubscriptions)
        {
            DependencyFlowTestDriver.GetGraphAndCompare(testName, driver =>
            {
                return driver.GetDependencyFlowGraph(channelName, includeBuildTimes, includedFrequencies, includeDisabledSubscriptions);
            });
        }
    }
}
