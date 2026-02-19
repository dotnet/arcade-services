using AwesomeAssertions;
using Microsoft.Internal.Helix.BuildFailureAnalysis.Models;
using NUnit.Framework;

namespace Microsoft.Internal.Helix.BuildFailureAnalysis.Tests.Models
{
    [TestFixture]
    public class FailureRateTests
    {

        [TestCase(0,0,null)]
        [TestCase(5,10,.5)]
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
