// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using AwesomeAssertions;
using Microsoft.DotNet.ProductConstructionService.Client.Models;
using NUnit.Framework;
using ProductConstructionService.BarViz.Code.Helpers;

namespace ProductConstructionService.BarViz.Tests;

[TestFixture]
public class SubscriptionOutcomeHelperTests
{
    [TestCase(OutcomeType.Updated)]
    [TestCase(OutcomeType.Approved)]
    public void GetOutcomeTypeBadgeColor_SuccessfulOutcome_ReturnsGreen(OutcomeType type)
    {
        SubscriptionOutcomeHelper.GetOutcomeTypeBadgeColor(type).Should().Be("status-green");
    }

    [Test]
    public void GetSubscriptionOutcomeTypeText_Approved_ReturnsApproved()
    {
        SubscriptionOutcomeHelper.GetSubscriptionOutcomeTypeText(OutcomeType.Approved).Should().Be("Approved");
    }
}
