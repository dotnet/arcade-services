// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text;

namespace BuildInsights.BuildAnalysis.Models;

public class BuildAnalysisUpdateOverridenResult
{
    public const string OverrideResultIdentifier = @"<!--  Build Analysis Check Run Override -->";

    public string Reason { get; set; }
    public string PreviousResult { get; set; }
    public string NewResult { get; set; }
    public string CheckResultBody { get; set; }


    public BuildAnalysisUpdateOverridenResult(string reason, string previousResult, string newResult, string? checkResultBody)
    {
        Reason = reason;
        PreviousResult = previousResult;
        NewResult = newResult;
        CheckResultBody = GetCheckResultWithOutPreviousOverrideInformation(checkResultBody);
    }

    private static string GetCheckResultWithOutPreviousOverrideInformation(string? checkResultBody)
    {
        if (string.IsNullOrEmpty(checkResultBody))
        {
            return string.Empty;
        }

        var bodyWithValidation = new StringBuilder();
        bodyWithValidation.AppendLine(OverrideResultIdentifier);

        int indexOfIdentifier = checkResultBody.LastIndexOf(OverrideResultIdentifier, StringComparison.Ordinal);

        indexOfIdentifier = indexOfIdentifier > 0 ? indexOfIdentifier + OverrideResultIdentifier.Length : indexOfIdentifier;
        string bodyWithNoPreviousOverride = indexOfIdentifier > 0 ? checkResultBody[indexOfIdentifier..] : checkResultBody;

        bodyWithValidation.AppendLine(bodyWithNoPreviousOverride);

        return bodyWithValidation.ToString();
    }
}
