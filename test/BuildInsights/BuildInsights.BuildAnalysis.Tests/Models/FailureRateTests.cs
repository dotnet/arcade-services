// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using AwesomeAssertions;
using BuildInsights.BuildAnalysis.Models;
using NUnit.Framework;

namespace BuildInsights.BuildAnalysis.Tests.Models
{
    [TestFixture]
    public class FailureRateTests
    {

        [TestCase(0, 0, null)]
        [TestCase(5, 10, .5)]
        [TestCase(0, 10, 0)]
        [TestCase(10, 10, 1)]
        public void FailureRatePercentageTest(int failedRuns, int totalRuns, double? percentage)
        {
            var failureRate = new FailureRate()
            {
                FailedRuns = failedRuns,
                TotalRuns = totalRuns
            };

            failureRate.PercentageOfFailure.Should().Be(percentage);
        }
    }
}
