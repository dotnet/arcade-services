using System.Linq;
using AwesomeAssertions;
using BuildInsights.KnownIssues;
using BuildInsights.KnownIssues.Models;
using NUnit.Framework;

namespace BuildInsights.KnownIssues.Tests
{
    [TestFixture]
    public class KnownIssueHelperTests
    {
        [Test]
        [TestCase("```\r\n{\r\n    \"errorMessage\" : \"ran longer than the maximum time\"\r\n}\r\n```")]
        [TestCase("```json\r\n{\r\n    \"errorMessage\" : \"ran longer than the maximum time\"\r\n}\r\n```")]
        [TestCase("```json\r\n{\r\n    \"ErrorMessage\" : \"ran longer than the maximum time\"\r\n}\r\n```")]
        public void ValidateJson(string json)
        {
            KnownIssueJson knownIssueJson = KnownIssueHelper.GetKnownIssueJson(json);
            knownIssueJson.ErrorMessage.First().Should().Be("ran longer than the maximum time");
            knownIssueJson.BuildRetry.Should().BeFalse();
        }

        [Test]
        [TestCase("```\r\n{\r\n    \"errorMessage\" : \"ran longer than the maximum time\",\r\n   \"BuildRetry\":true\r\n}\r\n```")]
        [TestCase("```json\r\n{\r\n  \"ErrorMessage\": \"ran longer than the maximum time\",\r\n  \"BuildRetry\":true\r\n}\r\n```")]
        [TestCase("```json\r\n{\r\n  \"errorMessage\": \"ran longer than the maximum time\",\r\n  \"buildRetry\":true\r\n}\r\n```")]
        public void ValidateKnownIssueJson(string json)
        {
            KnownIssueJson knownIssueJson = KnownIssueHelper.GetKnownIssueJson(json);
            knownIssueJson.ErrorMessage.First().Should().Be("ran longer than the maximum time");
            knownIssueJson.BuildRetry.Should().BeTrue();
        }

        [Test]
        [TestCase("```json\r\n{\r\n  \"ErrorMessage\": [\"Assert.True() Failure\", \"Expected: True\", \"Actual: False\"]\r\n}\r\n```")]
        [TestCase("```json\r\n{\r\n  \"errorMessage\": [\"Assert.True() Failure\", \"Expected: True\", \"Actual: False\"]\r\n}\r\n```")]
        public void ValidateMultiLineErrorMessageKnownIssueJson(string json)
        {
            KnownIssueJson knownIssueJson = KnownIssueHelper.GetKnownIssueJson(json);
            knownIssueJson.ErrorMessage.Should().HaveCount(3);
            knownIssueJson.ErrorMessage.First().Should().Be("Assert.True() Failure");
            knownIssueJson.ErrorMessage.ElementAt(1).Should().Be("Expected: True");
            knownIssueJson.ErrorMessage.Last().Should().Be("Actual: False");
            knownIssueJson.BuildRetry.Should().BeFalse();
        }

        [Test]
        [TestCase("```json\r\n{\r\n  \"ErrorPattern\": [\"ran longer.*\", \"than the maximum.*\", \"time.*\"]\r\n}\r\n```")]
        public void ValidateMultiLineErrorPatternKnownIssueJson(string json)
        {
            KnownIssueJson knownIssueJson = KnownIssueHelper.GetKnownIssueJson(json);
            knownIssueJson.ErrorPattern.Should().HaveCount(3);
            knownIssueJson.ErrorPattern.First().Should().Be("ran longer.*");
            knownIssueJson.ErrorPattern.ElementAt(1).Should().Be("than the maximum.*");
            knownIssueJson.ErrorPattern.Last().Should().Be("time.*");
            knownIssueJson.BuildRetry.Should().BeFalse();
        }

        [TestCase(true, "\"ErrorPattern\": \"ANY_ERROR_MESSAGE\"", "\"ErrorMessage\": \"ANY_ERROR_MESSAGE\"")]
        [TestCase(false, "\"ErrorMessage\": \"ANY_ERROR_MESSAGE\"", "\"ErrorPattern\": \"ANY_ERROR_MESSAGE\"")]
        public void GetKnownIssueFilledInErrorPatternTest(bool isErrorPattern, string expectedResult, string notContain)
        {
            string knownIssue = KnownIssueHelper.GetKnownIssueJsonFilledIn("ANY_ERROR_MESSAGE", isErrorPattern, false, false);
            knownIssue.Should().Contain(expectedResult);
            knownIssue.Should().NotContain(notContain);
        }

        [Test]
        public void GetKnownIssueFilledInErrorMessageWithSpecialCharacters()
        {
            string knownIssue = KnownIssueHelper.GetKnownIssueJsonFilledIn(@"\d+ \(\d+\) on device \(error\: 1\)", true, false, false);
            knownIssue.Should().Contain(@"\\d+ \\(\\d+\\) on device \\(error\\: 1\\");
        }
    }
}
