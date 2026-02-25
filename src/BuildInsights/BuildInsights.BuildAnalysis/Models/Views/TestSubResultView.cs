// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.TeamFoundation.Common;

namespace BuildInsights.BuildAnalysis.Models.Views;

public class TestSubResultView
{
    public string TestName { get; }
    public string ErrorMessage { get; }
    public string StackTrace { get; }

    public TestSubResultView(string testName, string errorMessage, string stackTrace)
    {
        TestName = testName;
        ErrorMessage = errorMessage;
        StackTrace = stackTrace;
    }

    //The SubResult name tends to be made up of the name of the test + data driven info
    private static string CreateTestSubResultName(string testName, string subResultDisplayName)
    {
        if (testName.IsNullOrEmpty())
        {
            return subResultDisplayName;
        }

        return subResultDisplayName.Replace(testName, "");
    }
}
