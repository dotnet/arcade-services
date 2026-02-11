// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Immutable;
using BuildInsights.KnownIssues.Models;

namespace BuildInsights.BuildAnalysis.Models;

public interface IResult
{
    public FailureRate FailureRate { get; }
    public IImmutableList<KnownIssue> KnownIssues { get; }
}
