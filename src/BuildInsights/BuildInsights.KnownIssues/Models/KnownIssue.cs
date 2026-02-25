// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using BuildInsights.GitHub.Models;

namespace BuildInsights.KnownIssues.Models;

public class KnownIssue
{
    public GitHubIssue GitHubIssue { get; set; }
    public KnownIssueOptions Options { get; set; }
    public KnownIssueType IssueType { get; set; }
    [JsonConverter(typeof(ErrorOrArrayOfErrorsConverter))]
    public List<string> BuildError { get; set; }

    public Func<string, bool> IsMatch;
    public Func<string, int, bool> IsMatchByErrorPosition;
    public KnownIssueBuildErrorsType BuildErrorsType { get; }

    private readonly List<Regex> _matchingRegexes = [];

    public KnownIssue(GitHubIssue githubIssue, List<string> buildErrors, KnownIssueType issueType, KnownIssueOptions options)
    {
        GitHubIssue = githubIssue;
        IssueType = issueType;
        Options = options;
        BuildError = buildErrors;
        BuildErrorsType = buildErrors?.Count > 1 ? KnownIssueBuildErrorsType.Multiline : KnownIssueBuildErrorsType.SingleLine;

        if (options.RegexMatching)
        {
            foreach (string buildError in BuildError)
            {
                _matchingRegexes.Add(new Regex(buildError, RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.NonBacktracking, TimeSpan.FromMilliseconds(20)));
            }

            IsMatch = (string line) => BuildErrorsType == KnownIssueBuildErrorsType.SingleLine && _matchingRegexes.First().IsMatch(line);
            IsMatchByErrorPosition = (string line, int errorPosition) => errorPosition < BuildError.Count && _matchingRegexes.ElementAt(errorPosition).IsMatch(line);
        }
        else
        {
            IsMatch = (string line) => BuildErrorsType == KnownIssueBuildErrorsType.SingleLine && line.Contains(BuildError.First());
            IsMatchByErrorPosition = (string line, int errorPosition) => errorPosition < BuildError.Count && line.Contains(BuildError.ElementAt(errorPosition));
        }
    }

    public bool IsLastError(int errorPosition)
    {
        return errorPosition == BuildError.Count - 1;
    }
}

public enum KnownIssueType
{
    Infrastructure = 0,
    Repo = 1,
    Critical = 2,
    Test = 3,
    Validation = 4
}
