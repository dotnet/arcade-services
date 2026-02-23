using System.Collections.Generic;
using AwesomeAssertions;
using BuildInsights.BuildAnalysis.Models;
using BuildInsights.BuildAnalysis.Models.Views;
using NUnit.Framework;

namespace BuildInsights.BuildAnalysis.Tests.Providers;

[TestFixture]
public class TestGroupViewHelperTests
{
    [Test]
    public void AllTheTestsFirstPipelineTest()
    {
        int limitOfTestToDisplay = TestGroupViewHelper.LimitOfTestToDisplay;

        TestResultsGroupView groupViewA = MockTestResultsGroupView("A", MockListTestResultView(limitOfTestToDisplay));
        TestResultsGroupView groupViewB = MockTestResultsGroupView("B", MockListTestResultView(1));
        TestResultsGroupView groupViewC = MockTestResultsGroupView("C", MockListTestResultView(3));

        var testResultsGroupViews = new List<TestResultsGroupView> {groupViewA, groupViewB, groupViewC};

        List<TestResultsGroupView> result = TestGroupViewHelper.DistributeDisplayedTestResults(testResultsGroupViews);
        result[0].DisplayTestsCount.Should().Be(limitOfTestToDisplay);
        result[1].DisplayTestsCount.Should().Be(0);
        result[2].DisplayTestsCount.Should().Be(0);
    }

    [Test]
    public void TestsDividedAcrossPipelinesTest()
    {
        TestResultsGroupView groupViewA = MockTestResultsGroupView("A", MockListTestResultView(15));
        TestResultsGroupView groupViewB = MockTestResultsGroupView("B", MockListTestResultView(15));

        var testResultsGroupViews = new List<TestResultsGroupView> {groupViewA, groupViewB};
        List<TestResultsGroupView> result = TestGroupViewHelper.DistributeDisplayedTestResults(testResultsGroupViews);
        result[0].DisplayTestsCount.Should().Be(3);
        result[1].DisplayTestsCount.Should().Be(2);
    }

    [Test]
    public void TestsDividedProperlyToShowAllPossibleTests()
    {
        TestResultsGroupView groupViewA = MockTestResultsGroupView("A", MockListTestResultView(1));
        TestResultsGroupView groupViewB = MockTestResultsGroupView("B", MockListTestResultView(2));
        TestResultsGroupView groupViewC = MockTestResultsGroupView("C", MockListTestResultView(4));

        var testResultsGroupViews = new List<TestResultsGroupView> {groupViewA, groupViewB, groupViewC};
        List<TestResultsGroupView> result = TestGroupViewHelper.DistributeDisplayedTestResults(testResultsGroupViews);
        result[0].DisplayTestsCount.Should().Be(1);
        result[1].DisplayTestsCount.Should().Be(2);
        result[2].DisplayTestsCount.Should().Be(2);
    }

    [Test]
    public void TestsAreOrderAndPrioritizedByName()
    {
        TestResultsGroupView groupViewC = MockTestResultsGroupView("C", MockListTestResultView(3));
        TestResultsGroupView groupViewB = MockTestResultsGroupView("B", MockListTestResultView(2));
        TestResultsGroupView groupViewA = MockTestResultsGroupView("A", MockListTestResultView(4));

        var testResultsGroupViews = new List<TestResultsGroupView> {groupViewA, groupViewB, groupViewC};

        List<TestResultsGroupView> result = TestGroupViewHelper.DistributeDisplayedTestResults(testResultsGroupViews);
        result[0].PipelineName.Should().Be("A");
        result[0].DisplayTestsCount.Should().Be(4);

        result[1].PipelineName.Should().Be("B");
        result[1].DisplayTestsCount.Should().Be(1);

        result[2].PipelineName.Should().Be("C");
        result[2].DisplayTestsCount.Should().Be(0);
    }

    private TestResultsGroupView MockTestResultsGroupView(string pipelineName, List<TestResultView> testResultViews)
    {
        return new TestResultsGroupView("ANY_LINK", pipelineName, testResultViews, testResultViews.Count);
    }

    private List<TestResultView> MockListTestResultView(int countTest)
    {
        var testResultViews = new List<TestResultView>();
        for (int i = 0; i < countTest; i++)
        {
            testResultViews.Add(new TestResultView());
        }

        return testResultViews;
    }
}
