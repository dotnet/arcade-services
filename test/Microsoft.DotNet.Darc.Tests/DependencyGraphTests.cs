// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.IO;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Microsoft.DotNet.Darc.Tests;

[TestFixture]
public class DependencyGraphTests
{
    [TestCase("core-sdk", "c57bde45936fc120c0124bf964f570c45bfd8f8b", true)]
    [TestCase("core-sdk", "c57bde45936fc120c0124bf964f570c45bfd8f8b", false)]
    [TestCase("core-sdk", "36e48aa03cb568c128f387a090ad5ece86933c1c", true)]
    [TestCase("core-sdk", "36e48aa03cb568c128f387a090ad5ece86933c1c", false)]
    [TestCase("corefx", "bc0cfc49d1a8c1681fb09603f3389ef65342d542", true)]
    [TestCase("corefx", "bc0cfc49d1a8c1681fb09603f3389ef65342d542", false)]
    public async Task ValidateGraph(string rootRepo, string rootCommit, bool includeToolset)
    {
        await DependencyTestDriver.GetGraphAndCompare("DependencyGraph",
            driver => driver.GetDependencyGraph(rootRepo, rootCommit, includeToolset),
            Path.Combine(rootRepo,
                rootCommit,
                includeToolset ? "graph-with-toolset.xml" : "graph-without-toolset.xml")
        );
    }
}
