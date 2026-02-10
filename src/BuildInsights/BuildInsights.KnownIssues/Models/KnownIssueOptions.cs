// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace BuildInsights.KnownIssues.Models;

public class KnownIssueOptions
{
    public bool ExcludeConsoleLog { get; }

    public bool RetryBuild { get; }

    public bool RegexMatching { get; }

    public KnownIssueOptions(bool excludeConsoleLog = default, bool retryBuild = default, bool regexMatching = default)
    {
        ExcludeConsoleLog = excludeConsoleLog;
        RetryBuild = retryBuild;
        RegexMatching = regexMatching;
    }
}
