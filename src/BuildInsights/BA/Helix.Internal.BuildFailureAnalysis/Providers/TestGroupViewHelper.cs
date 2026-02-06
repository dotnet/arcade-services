using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Internal.Helix.BuildFailureAnalysis.Models;

namespace Microsoft.Internal.Helix.BuildFailureAnalysis.Providers;

public class TestGroupViewHelper
{
    public const int LimitOfTestToDisplay = 5;

    public static List<TestResultsGroupView> DistributeDisplayedTestResults(List<TestResultsGroupView> testResultsGroupViews)
    {
        List<TestResultsGroupView> resultsGroupViewsOrdered = testResultsGroupViews.OrderBy(t => t.PipelineName).ToList();

        int remainingSpotsForTests = LimitOfTestToDisplay;

        for (int resultGroup = 0; resultGroup < resultsGroupViewsOrdered.Count; resultGroup++)
        {
            int testsPerGroup;
            if (resultsGroupViewsOrdered[resultGroup].TestResults.Count <= remainingSpotsForTests)
            {
                testsPerGroup = resultsGroupViewsOrdered[resultGroup].TestResults.Count;
            }
            else
            {
                int remainingGroups = resultsGroupViewsOrdered.Count - resultGroup;
                testsPerGroup = (int) Math.Ceiling((double) remainingSpotsForTests / remainingGroups);
                testsPerGroup = testsPerGroup >= 0 ? testsPerGroup : 0;
            }

            resultsGroupViewsOrdered[resultGroup].DisplayTestsCount = testsPerGroup;
            remainingSpotsForTests -= testsPerGroup;
        }

        return resultsGroupViewsOrdered;
    }
}
