using System;
using System.Collections.Generic;
using System.Reactive.Disposables;
using AwesomeAssertions;
using Microsoft.Internal.Helix.KnownIssues.Models;
using NUnit.Framework;

namespace Helix.Internal.KnownIssues.Tests;

[TestFixture]
public class KnownIssueTests
{
    [Test]
    public void KnownIssueSingleErrorCreationTest()
    {
        KnownIssue knownIssue = new KnownIssue(null, new List<string>(){ "Assert.True() Failure" }, KnownIssueType.Infrastructure, new KnownIssueOptions());
        knownIssue.BuildErrorsType.Should().Be(KnownIssueBuildErrorsType.SingleLine);
    }

    [TestCase("Assert.True() Failure", true)]
    [TestCase("Expected: True", false)]
    public void KnownIssueSingleErrorMessage(string errorMessage, bool expectedResult)
    {
        string errorLine = "Assert.True() Failure";
        KnownIssue knownIssue = new KnownIssue(null, new List<string>() { errorMessage}, KnownIssueType.Infrastructure, new KnownIssueOptions());
        knownIssue.IsMatch(errorLine).Should().Be(expectedResult);
    }

    [Test]
    public void KnownIssueSingleMessageMatchWithPositionTest()
    {
        string errorLine = "Assert.True() Failure";
        KnownIssue knownIssue = new KnownIssue(null, new List<string>() { errorLine }, KnownIssueType.Infrastructure, new KnownIssueOptions());
        knownIssue.IsMatchByErrorPosition(errorLine, 0).Should().BeTrue();
    }

    [Test]
    public void KnownIssueSingleRegexMatchTest()
    {
        string errorLine = "Assert.True() Failure";
        string regex = ".* Failure";
        KnownIssue knownIssue = new KnownIssue(null, new List<string>() { regex }, KnownIssueType.Infrastructure, new KnownIssueOptions(regexMatching:true));
        knownIssue.IsMatch(errorLine).Should().BeTrue();
    }

    [Test]
    public void KnownIssueMultiErrorMessageSingleMatchShouldFailTest()
    {
        string errorLine = "Assert.True() Failure";
        KnownIssue knownIssue = new KnownIssue(null, new List<string>() { errorLine, "Expected: True" }, KnownIssueType.Infrastructure, new KnownIssueOptions());
        knownIssue.IsMatch(errorLine).Should().BeFalse();
    }

    [Test]
    public void KnownIssueMultiErrorMessageCreationTest()
    {
        KnownIssue knownIssue = new KnownIssue(null, new List<string>() { "Assert.True() Failure", "Expected: True" }, KnownIssueType.Infrastructure, new KnownIssueOptions());
        knownIssue.BuildErrorsType.Should().Be(KnownIssueBuildErrorsType.Multiline);
    }

    [TestCase("Assert.True() Failure", "Expected: True", 0, true)]
    [TestCase("Expected: True", "Assert.True() Failure", 1, true)]
    [TestCase("Expected: True", "Expected: False", 0, false)]
    public void KnownIssueMultiErrorMessageWithPositionTest(string firstErrorMessage, string secondErrorMessage, int position, bool expectedResult)
    {
        string errorLine = "Assert.True() Failure";
        KnownIssue knownIssue = new KnownIssue(null, new List<string>() { firstErrorMessage, secondErrorMessage }, KnownIssueType.Infrastructure, new KnownIssueOptions());
        knownIssue.IsMatchByErrorPosition(errorLine, position).Should().Be(expectedResult);
    }

    [Test]
    public void KnownIssueMultiRegexSingleMatchWithPositionTest()
    {
        string errorLine = "Assert.True() Failure";
        KnownIssue knownIssue = new KnownIssue(null, new List<string>() { "Assert.*Failure", "Expected:.*" }, KnownIssueType.Infrastructure, new KnownIssueOptions(regexMatching: true));
        knownIssue.IsMatchByErrorPosition(errorLine, 0).Should().BeTrue();
    }

    [Test]
    public void KnownIssueSingleRegexMatchWithPositionTest()
    {
        string errorLine = "Assert.True() Failure";
        string regex = ".* Failure";
        KnownIssue knownIssue = new KnownIssue(null, new List<string>() { regex }, KnownIssueType.Infrastructure, new KnownIssueOptions(regexMatching: true));
        knownIssue.IsMatchByErrorPosition(errorLine, 0).Should().BeTrue();
    }

    [TestCase(0, false)]
    [TestCase(1, true)]
    public void KnownIssueIsLastErrorTest(int position, bool expectedResult)
    {
        KnownIssue knownIssue = new KnownIssue(null, new List<string>() { "Assert.True() Failure", "Expected: True" }, KnownIssueType.Infrastructure, new KnownIssueOptions());
        knownIssue.IsLastError(position).Should().Be(expectedResult);
    }
}
