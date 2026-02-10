// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Immutable;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using BuildInsights.GitHub.Models;
using BuildInsights.KnownIssues.Models;
using Octokit;

namespace BuildInsights.KnownIssues;

public class KnownIssueHelper
{
    static readonly string ErrorMessageRegex = @"```(json)?\s*({.*})\s*```";
    public static readonly string StartKnownIssueValidationIdentifier = "\r\n<!-- Known issue validation start -->";
    public static readonly string EndKnownIssueValidationIdentifier = "<!-- Known issue validation end -->";
    public static readonly string StartKnownIssueReportIdentifier = "\r\n<!--Known issue error report start -->";
    public static readonly string EndKnownIssueReportIdentifier = "<!--Known issue error report end -->";
    public static readonly string ErrorMessageTemplateIdentifier = "<!-- Error message template  -->";

    public static readonly string KnownIssuesDocumentation = "https://github.com/dotnet/arcade/blob/main/Documentation/Projects/Build%20Analysis/KnownIssueJsonStepByStep.md";
    /// <summary>
    /// Looks for valid json strings in the issue body
    /// If it finds more than one json string with an error message,
    /// it will return the last it finds
    /// </summary>
    /// <param name="issueBody"></param>
    /// <returns>The KnownIssueJson found in the last valid json in the issue body</returns>
    public static KnownIssueJson GetKnownIssueJson(string issueBody)
    {
        var knownIssueJson = new KnownIssueJson();
        if (string.IsNullOrEmpty(issueBody))
            return knownIssueJson;

        RegexOptions regexOptions = RegexOptions.Singleline;
        var serializerOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        foreach (Match match in Regex.Matches(issueBody, ErrorMessageRegex, regexOptions))
        {
            string jsonString = match.Groups[2].Value;
            try
            {
                KnownIssueJson json = JsonSerializer.Deserialize<KnownIssueJson>(jsonString, serializerOptions);
                if (json?.ErrorMessage != null || json?.ErrorPattern != null)
                    knownIssueJson = json;
            }
            catch
            {
                continue;
            }
        }

        return knownIssueJson;
    }

    public static KnownIssue ParseGithubIssue(Issue githubIssue, string repository, KnownIssueType issueType)
    {
        GitHubIssue gitHubIssue = new GitHubIssue(
            id: githubIssue.Number,
            title: githubIssue.Title,
            repositoryWithOwner: repository,
            body: githubIssue.Body,
            linkGitHubIssue: githubIssue.HtmlUrl,
            labels: githubIssue.Labels?.Select(l => l.Name).ToList() ?? new List<string>());

        KnownIssueJson knownIssueJson = GetKnownIssueJson(githubIssue.Body);
        if (knownIssueJson.ErrorPattern is {Count: > 0})
        {
            return new KnownIssue(gitHubIssue, knownIssueJson.ErrorPattern, issueType, new(knownIssueJson.ExcludeConsoleLog, knownIssueJson.BuildRetry, regexMatching: true));
        }
        return new KnownIssue(gitHubIssue, knownIssueJson.ErrorMessage, issueType, new(knownIssueJson.ExcludeConsoleLog, knownIssueJson.BuildRetry, regexMatching: false));
    }

    public static string GetReportIssueUrl(Dictionary<string, string> parameters, IssueParameters issueParameters, string host, string repository, string pullRequest)
    {
        if (!string.IsNullOrEmpty(issueParameters?.GithubTemplateName))
        {
            parameters.Add("template", issueParameters.GithubTemplateName);
        }

        if (issueParameters?.Labels?.Count > 0)
        {
            parameters.Add("labels", string.Join(",", issueParameters.Labels));
        }

        return CreateUrl(host, parameters.ToImmutableDictionary());
    }

    public static string CreateUrl(string host, ImmutableDictionary<string, string> parameters)
    {
        var url = new StringBuilder(host);
        if (string.IsNullOrEmpty(host) || !host.EndsWith('?'))
        {
            url.Append('?');
        }
        bool added = false;
        foreach ((string key, string value) in parameters)
        {
            if (added)
            {
                url.Append('&');
            }

            added = true;

            url.Append(Uri.EscapeDataString(key));
            url.Append('=');
            url.Append(Uri.EscapeDataString(value));
        }

        return url.ToString();
    }

    public static string GetKnownIssueSectionTemplate()
    {
        return $@"
Fill the error message using [step by step known issues guidance]({KnownIssuesDocumentation}).

<!-- Use ErrorMessage for String.Contains matches. Use ErrorPattern for regex matches (single line/no backtracking). Set BuildRetry to `true` to retry builds with this error. Set ExcludeConsoleLog to `true` to skip helix logs analysis. -->

```json
{{
  ""ErrorMessage"": """",
  ""ErrorPattern"": """",
  ""BuildRetry"": false,
  ""ExcludeConsoleLog"": false
}}
```
";
    }

    public static string GetKnownIssueJsonFilledIn(string errorMessage, bool isErrorPattern, bool isBuildRetry, bool isExcludeLog)
    {
        var options = new JsonSerializerOptions
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };
        errorMessage = JsonSerializer.Serialize(errorMessage, options);
        string errorMessageJson = isErrorPattern ? $"\"ErrorPattern\": {errorMessage}" : $"\"ErrorMessage\": {errorMessage}";

        string buildRetry = isBuildRetry ? "true" : "false";
        string excludeLog = isExcludeLog ? "true" : "false";

        return $@"
Fill the error message using [step by step known issues guidance]({KnownIssuesDocumentation}).

<!-- Use ErrorMessage for String.Contains matches. Use ErrorPattern for regex matches (single line/no backtracking). Set BuildRetry to `true` to retry builds with this error. Set ExcludeConsoleLog to `true` to skip helix logs analysis. -->

```json
{{
  {errorMessageJson},
  ""BuildRetry"": {buildRetry},
  ""ExcludeConsoleLog"": {excludeLog}
}}
```
";
    }

    public static string GetKnownIssueErrorMessageStringConversion(List<string> errorMessages)
    {
        return errorMessages == null ? string.Empty : string.Join(" ", errorMessages);
    }
}
