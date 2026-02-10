// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using BuildInsights.KnownIssues.Models;
using Microsoft.Extensions.Options;

namespace BuildInsights.KnownIssues;

public interface IKnownIssuesMatchService
{
    Task<List<KnownIssue>> GetKnownIssuesInStream(Stream stream, IReadOnlyList<KnownIssue> knownIssues);
    List<KnownIssue> GetKnownIssuesInString(string errorLine, IReadOnlyList<KnownIssue> knownIssues);
}

public class KnownIssuesMatchProvider : IKnownIssuesMatchService
{
    private readonly KnownIssuesAnalysisLimits _analysisLimits;

    public KnownIssuesMatchProvider(IOptions<KnownIssuesAnalysisLimits> analysisLimits)
    {
        _analysisLimits = analysisLimits.Value;
    }

    public async Task<List<KnownIssue>> GetKnownIssuesInStream(Stream stream, IReadOnlyList<KnownIssue> knownIssues)
    {
        Dictionary<KnownIssue, int> knownIssuesErrorPositionUnderAnalysis = knownIssues.ToDictionary(k => k, k => 0);
        using var reader = new StreamReader(stream);

        int lineCount = 0;
        var knownIssueMatched = new List<KnownIssue>();
        while (await reader.ReadLineAsync() is { } logLine && lineCount <= _analysisLimits.LogLinesCountLimit)
        {
            foreach (KnownIssue knownIssue in knownIssues)
            {
                knownIssuesErrorPositionUnderAnalysis.TryGetValue(knownIssue, out int errorPosition);

                if (knownIssue.IsMatchByErrorPosition(logLine, errorPosition))
                {
                    if (knownIssue.IsLastError(errorPosition))
                    {
                        knownIssueMatched.Add(knownIssue);
                    }
                    else
                    {
                        knownIssuesErrorPositionUnderAnalysis[knownIssue]++;
                    }
                }
            }

            lineCount++;
        }

        return knownIssueMatched;
    }

    public List<KnownIssue> GetKnownIssuesInString(string errorLine, IReadOnlyList<KnownIssue> knownIssues)
    {
        if (string.IsNullOrEmpty(errorLine))
        {
            return [];
        }

        var knownIssuesWithSingleLineError = knownIssues.Where(k => k.BuildErrorsType == KnownIssueBuildErrorsType.SingleLine);
        List<KnownIssue> knownIssuesWithMultilineLineError = [..knownIssues.Where(k => k.BuildErrorsType == KnownIssueBuildErrorsType.Multiline)];
        List<KnownIssue> knownIssueMatched = [..knownIssuesWithSingleLineError.Where(knownIssue => knownIssue.IsMatch(errorLine))];

        string[] lines = errorLine.Split(["\r\n", "\r", "\n"], StringSplitOptions.None);
        if (lines.Length > 1)
        {
            Dictionary<KnownIssue, int> knownIssuesErrorPositionUnderAnalysis = knownIssuesWithMultilineLineError.ToDictionary(k => k, k => 0);
            foreach (string line in lines)
            {
                foreach (KnownIssue knownIssue in knownIssuesWithMultilineLineError)
                {
                    knownIssuesErrorPositionUnderAnalysis.TryGetValue(knownIssue, out int errorPosition);
                    if (knownIssue.IsMatchByErrorPosition(line, errorPosition))
                    {
                        if (knownIssue.IsLastError(errorPosition))
                        {
                            knownIssueMatched.Add(knownIssue);
                        }
                        else
                        {
                            knownIssuesErrorPositionUnderAnalysis[knownIssue]++;
                        }
                    }
                }
            }
        }

        return knownIssueMatched;
    }
}
