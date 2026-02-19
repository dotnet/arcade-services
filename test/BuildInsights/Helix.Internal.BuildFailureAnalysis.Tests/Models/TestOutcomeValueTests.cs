using AwesomeAssertions;
using Microsoft.Internal.Helix.BuildFailureAnalysis.Models;
using NUnit.Framework;

namespace Microsoft.Internal.Helix.BuildFailureAnalysis.Tests.Models
{
    [TestFixture]
    public class TestOutcomeValueTests
    {
        [TestCase("Failed", "Failed", true)]
        [TestCase("Failed", "Passed", false)]
        [TestCase(null, "Failed", false)]
        [TestCase("Failed", null, false)]
        public void TestOutcomeValueEqualsParse(string valueA, string valueB, bool expectedResult)
        {
            TestOutcomeValue outcomeA = TestOutcomeValue.Parse(valueA);
            TestOutcomeValue outcomeB = TestOutcomeValue.Parse(valueB);

            outcomeA.Equals(outcomeB).Should().Be(expectedResult);
        }

        [TestCase("Failed", "Failed", true)]
        [TestCase("Failed", "Passed", false)]
        public void TestOutcomeStringComparer(string valueA, string valueB, bool expectedResult)
        {
            TestOutcomeValue outcomeA = TestOutcomeValue.Parse(valueA);
            outcomeA.Equals(valueB).Should().Be(expectedResult);
        }

        [TestCase("Failed", true)]
        [TestCase("Succeeded", false)]
        public void StringEqualTestOutcomeValue(string outcomeStr, bool expectedResult)
        {
            TestOutcomeValue outcomeA = TestOutcomeValue.Failed;
            bool result = outcomeStr == outcomeA;

            result.Should().Be(expectedResult);
        }

        [TestCase("Failed", false)]
        [TestCase("Succeeded", true)]
        public void TestOutcomeValueNotEqualString(string outcomeStr, bool expectedResult)
        {
            TestOutcomeValue outcomeValue = TestOutcomeValue.Failed;
            bool result = outcomeValue != outcomeStr;

            result.Should().Be(expectedResult);
        }

        [TestCase("Failed", true)]
        [TestCase("Succeeded", false)]
        public void TestOutcomeValueEqualString(string outcomeStr, bool expectedResult)
        {
            TestOutcomeValue outcomeValue = TestOutcomeValue.Failed;
            bool result = outcomeValue == outcomeStr;

            result.Should().Be(expectedResult);
        }

        [TestCase("Failed", false)]
        [TestCase("Succeeded", true)]
        public void StringNotEqualTestOutcomeValue(string outcomeStr, bool expectedResult)
        {
            TestOutcomeValue outcomeValue = TestOutcomeValue.Failed;
            bool result = outcomeStr != outcomeValue;

            result.Should().Be(expectedResult);
        }

        [Test]
        public void TestOutcomeValueComparer()
        {
            TestOutcomeValue outcomeA = TestOutcomeValue.Failed;
            TestOutcomeValue outcomeB = TestOutcomeValue.Failed;

            outcomeA.Equals(outcomeB).Should().BeTrue();
        }

        [Test]
        public void TestOutcomeValueOperatorNotEqual()
        {
            TestOutcomeValue outcomeA = TestOutcomeValue.Failed;
            TestOutcomeValue outcomeB = TestOutcomeValue.Failed;
            bool result = outcomeA != outcomeB;

            result.Should().BeFalse();
        }


        [Test]
        public void StringRightTestOutcomeOperatorNullEqual()
        {
            TestOutcomeValue outcome = null;
            bool result = "Failed" == outcome;
            result.Should().BeFalse();
        }

        [Test]
        public void StringRightTestOutcomeOperatorNullNotEqual()
        {
            TestOutcomeValue outcome = null;
            bool result = "Failed" != outcome;
            result.Should().BeFalse();
        }

        [Test]
        public void TestOutcomeOperatorNullLeftStringEqual()
        {
            TestOutcomeValue outcome = null;
            bool result = outcome == "Failed";
            result.Should().BeFalse();
        }

        [Test]
        public void TestOutcomeOperatorNullLeftStringNotEqual()
        {
            TestOutcomeValue outcome = null;
            bool result = outcome != "Failed";
            result.Should().BeFalse();
        }
    }
}
