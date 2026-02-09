// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace BuildInsights.BuildAnalysis.Models;

public class MarkdownSummarizeInstructions
{
    //Apply rules to make the build analysis shorter (error message length limit, show less failures, etc.)
    public bool GenerateSummarizeVersion { get; }
    public int ErrorMessageLimitLength { get; }
    public int TestKnownIssueDisplayLimit { get;  }

    //Generate a summary version of build analysis, only showing number of errors and failing builds.
    public bool GenerateSummaryVersion { get;}

    public MarkdownSummarizeInstructions(bool generateSummarizeVersion, int errorMessageLimitLength, int testKnownIssueDisplayLimit)
    {
        GenerateSummarizeVersion = generateSummarizeVersion;
        ErrorMessageLimitLength = errorMessageLimitLength;
        TestKnownIssueDisplayLimit = testKnownIssueDisplayLimit;
    }

    public MarkdownSummarizeInstructions(bool generateSummaryVersion)
    {
        GenerateSummaryVersion = generateSummaryVersion;
    }
}
