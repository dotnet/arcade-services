// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using AwesomeAssertions;
using NUnit.Framework;

namespace BuildInsights.KnownIssues.Tests
{
    [TestFixture]
    public class KnownIssuesHistoryProviderTests
    {
        [Test]
        public void NormalizeIssueId()
        {
            string normalizedIssue = KnownIssuesHistoryProvider.NormalizeIssueId("dotnet/arcade", 12345);
            normalizedIssue.Should().Be("dotnet.arcade.12345");
        }
    }
}
