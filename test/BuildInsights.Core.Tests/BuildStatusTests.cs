// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using AwesomeAssertions;
using BuildInsights.Core;
using NUnit.Framework;

namespace BuildInsights.Core.Tests;

[TestFixture]
public class BuildStatusTests
{
    [Test]
    public void BuildStatus_HasExpectedValues()
    {
        var values = Enum.GetValues<BuildStatus>();

        values.Should().Contain(BuildStatus.Unknown);
        values.Should().Contain(BuildStatus.Succeeded);
        values.Should().Contain(BuildStatus.Failed);
        values.Should().Contain(BuildStatus.Cancelled);
        values.Should().Contain(BuildStatus.PartiallySucceeded);
    }
}
