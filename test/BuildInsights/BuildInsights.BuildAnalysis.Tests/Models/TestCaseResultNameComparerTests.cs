// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using AwesomeAssertions;
using BuildInsights.BuildAnalysis.Models;
using NUnit.Framework;

namespace BuildInsights.BuildAnalysis.Tests.Models;

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

    private static TestCaseResult MockTestCaseResult(string name)
    {
        return new TestCaseResult(name, new DateTimeOffset(2021, 5, 27, 11, 0, 0, 0, TimeSpan.Zero), TestOutcomeValue.Failed, 0, 0, 0, new PreviousBuildRef(), "", "", "", null, 55000);

    }
}
