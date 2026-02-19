using System;
using AwesomeAssertions;
using Microsoft.Internal.Helix.BuildFailureAnalysis.Models;
using NUnit.Framework;

namespace Microsoft.Internal.Helix.BuildFailureAnalysis.Tests.Models
{
    [TestFixture]
    public class TestCaseResultNameComparerTests
    {

        [TestCase("NameA", "NameA", true)]
        [TestCase("NameA", "NameB", false)]
        public void TestCaseResultNameComparerEqualsTest(string testNameA, string testNameB, bool expectedResult)
        {
            TestCaseResult A = MockTestCaseResult(testNameA);
            TestCaseResult B = MockTestCaseResult(testNameB);

            TestCaseResultNameComparer testCaseResultNameComparer = new TestCaseResultNameComparer();
            testCaseResultNameComparer.Equals(A, B).Should().Be(expectedResult);
        }

        [Test]
        public void TestCaseResultNameComparerEqualsNullValuesTest()
        {
            TestCaseResult A = MockTestCaseResult("TestA");

            TestCaseResultNameComparer testCaseResultNameComparer = new TestCaseResultNameComparer();
            testCaseResultNameComparer.Equals(A, null).Should().BeFalse();
        }

        private TestCaseResult MockTestCaseResult(string name)
        {
            return new TestCaseResult(name, new DateTimeOffset(2021, 5, 27, 11, 0, 0, 0, TimeSpan.Zero), TestOutcomeValue.Failed, 0, 0, 0, new PreviousBuildRef(), "", "", "", null, 55000);

        }
    }
}
