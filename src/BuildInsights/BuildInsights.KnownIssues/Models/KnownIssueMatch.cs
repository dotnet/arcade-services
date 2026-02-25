// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#nullable disable
namespace BuildInsights.KnownIssues.Models;

public class KnownIssueMatch : IEquatable<KnownIssueMatch>
{
    public int BuildId { get; set; }
    public string BuildRepository { get; set; }
    public int IssueId { get; set; }
    public string IssueRepository { get; set; }
    public string IssueType { get; set; }
    public string IssueLabels { get; set; }
    public string JobId { get; set; }
    public string StepName { get; set; }
    public string LogURL { get; set; }
    public string PullRequest { get; set; }
    public DateTimeOffset? StepStartTime { get; set; }
    public string Organization { get; set; }
    public string Project { get; set; }

    public bool Equals(KnownIssueMatch other)
    {
        if (other is null)
            return false;

        return IssueRepository == other.IssueRepository && IssueId == other.IssueId && JobId == other.JobId && BuildId == other.BuildId;
    }

    public override bool Equals(object obj) => Equals(obj as KnownIssueMatch);
    public override int GetHashCode() => (IssueRepository, IssueId, JobId, BuildId).GetHashCode();
}
