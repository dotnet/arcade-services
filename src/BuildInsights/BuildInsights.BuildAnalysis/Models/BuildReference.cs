// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;

namespace BuildInsights.BuildAnalysis.Models;

public class BuildReference
{
    public BuildReference() { }

    public DateTimeOffset? Date { get; set; }

    /// <summary>
    /// In the form 20210226.40
    /// </summary>
    public string BuildNumber { get; set; }
    public string BuildLink { get; set; }
    public string CommitLink { get; set; }
    public string CommitHash { get; set; }
}
