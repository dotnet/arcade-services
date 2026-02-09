// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;

namespace BuildInsights.KnownIssues.Models;

public class TestKnownIssueMatch : IEquatable<TestKnownIssueMatch>
{
    public int BuildId { get; set; }
    public string BuildRepository { get; set; }
    public int IssueId { get; set; }
    public string IssueRepository { get; set; }
    public string IssueType { get; set; }
    public string IssueLabels { get; set; }
    public string TestResultName { get; set; }
    public int TestRunId { get; set; }
    public string Url { get; set; }
    public string PullRequest { get; set; }
    public DateTimeOffset? CompletedDate { get; set; }
    public string Organization { get; set; }
    public string Project { get; set; }

    public bool Equals(TestKnownIssueMatch other)
    {
        if (other is null)
            return false;

        return IssueRepository == other.IssueRepository && IssueId == other.IssueId && TestResultName == other.TestResultName && TestRunId == other.TestRunId && BuildId == other.BuildId;
    }

    public override bool Equals(object obj) => Equals(obj as TestKnownIssueMatch);
    public override int GetHashCode() => (IssueRepository, IssueId, TestResultName, TestRunId, BuildId).GetHashCode();
}

