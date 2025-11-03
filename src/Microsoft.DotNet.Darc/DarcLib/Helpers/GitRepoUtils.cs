// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Maestro.Common;

namespace Microsoft.DotNet.DarcLib.Helpers;
public static class GitRepoUtils
{
    // Regex to extract PR number from a github merge commit message
    // e.g.: "Update dependencies from source-repo (#12345)" extracts "12345"
    private readonly static Regex GitHubPullRequestNumberExtractionRegex = new Regex(".+\\(#(\\d+)\\)$");

    // Alternative regex to extract PR number from a github merge commit message
    // e.g.: "Merge pull request #12345 from source-repo/branch" extracts "12345"
    private readonly static Regex AlternativeGitHubPullRequestNumberExtractionRegex = new Regex("^Merge pull request #(\\d+)");

    // Regex to extract PR number from an AzDO merge commit message
    // e.g.: "Merged PR 12345: Update dependencies from source-repo" extracts "12345"
    private readonly static Regex AzDoPullRequestNumberExtractionRegex = new Regex("^Merged PR (\\d+):");

    public static IReadOnlyList<(string title, string prUri)> ExtractPullRequestUrisFromCommitTitles    (
        IReadOnlyCollection<string> commitTitles,
        string gitRepoUrl)
    {
        var gitRepoType = GitRepoUrlUtils.ParseTypeFromUri(gitRepoUrl);
        // codeflow tests set the defaultRemote to a local path, we have to skip those
        if (gitRepoType == GitRepoType.Local)
        {
            return [];
        }

        (IReadOnlyList<Regex> regexes, string prLinkFormat) = gitRepoType switch
        {
            GitRepoType.GitHub => ((IReadOnlyList<Regex>)[GitHubPullRequestNumberExtractionRegex, AlternativeGitHubPullRequestNumberExtractionRegex], $"{gitRepoUrl}/pull/{{0}}"),
            GitRepoType.AzureDevOps => ([AzDoPullRequestNumberExtractionRegex], $"{gitRepoUrl}/pullrequest/{{0}}"),
            _ => throw new NotSupportedException($"Repository type for URI '{gitRepoUrl}' is not supported for PR link extraction.")
        };

        return commitTitles
            .Select(t =>
            {
                foreach (var regex in regexes)
                {
                    var match = regex.Match(t);
                    if (match.Success && match.Groups.Count > 1)
                    {
                        var prNumber = match.Groups[1].Value;
                        return (t, string.Format(prLinkFormat, prNumber));
                    }
                }
                return (null, null);
            })
            .Where(uri => !string.IsNullOrEmpty(uri.Item2))
            .ToList();
    }
}
