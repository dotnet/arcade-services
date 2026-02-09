// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.ComponentModel;
using Newtonsoft.Json;

namespace BuildInsights.KnownIssues.Models;

public class KnownIssueOptions
{
    [DefaultValue(false)]
    public bool ExcludeConsoleLog { get; }

    [DefaultValue(false)]
    public bool RetryBuild { get; }

    [DefaultValue(false)]
    public bool RegexMatching { get; }

    public KnownIssueOptions(bool excludeConsoleLog = default, bool retryBuild = default, bool regexMatching = default)
    {
        ExcludeConsoleLog = excludeConsoleLog;
        RetryBuild = retryBuild;
        RegexMatching = regexMatching;
    }
}
