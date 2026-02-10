// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace BuildInsights.KnownIssues.Models;

public class SsaCriteriaSettings
{
    public int DailyHitsForEscalation { get; set; }
    public List<string> SsaRepositories { get; set; }
    public string SsaLabel { get; set; }
}
