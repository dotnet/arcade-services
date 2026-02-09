// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;

namespace BuildInsights.BuildAnalysis.Models.Views;

public class KnownIssueView
{
    public string DisplayName { get; set; }
    public string Link { get; set; }
    public string IssueRepository { get; set; }
    public string IssueId { get; set; }
    public string LinkToGitHubIssue { get; set; }
    public string TitleGitHubIssue { get; set; }
    public int DuplicateHits { get; set; }
    public bool HasDuplicateHits => DuplicateHits > 0;

    public KnownIssueView(string displayName, string link, string issueRepository, string issueId, string linkToGitHubIssue, string titleGitHubIssue)
    {
        DisplayName = displayName;
        Link = link;
        IssueRepository = issueRepository;
        IssueId = issueId;
        LinkToGitHubIssue = linkToGitHubIssue;
        TitleGitHubIssue = titleGitHubIssue;
    }

    public KnownIssueView(KnownIssueView knownIssueView, int count)
    {
        DisplayName = knownIssueView.DisplayName;
        Link = knownIssueView.Link;
        IssueRepository = knownIssueView.IssueRepository;
        IssueId = knownIssueView.IssueRepository;
        LinkToGitHubIssue = knownIssueView.LinkToGitHubIssue;
        TitleGitHubIssue = knownIssueView.TitleGitHubIssue;
        DuplicateHits = count-1;
    }
}

public class KnownIssueViewComparer : IEqualityComparer<KnownIssueView>
{
    public bool Equals(KnownIssueView x, KnownIssueView y)
    {
        if (x == null && y == null)
        {
            return true;
        }

        if (x == null || y == null)
        {
            return false;
        }

        return x.Link.Equals(y.Link) &&
               x.IssueRepository.Equals(y.IssueRepository) &&
               x.IssueId.Equals(y.IssueId);
    }

    public int GetHashCode(KnownIssueView obj)
    {
        HashCode hash = new HashCode();
        hash.Add(obj.Link);
        hash.Add(obj.IssueRepository);
        hash.Add(obj.IssueId);
        return hash.ToHashCode();
    }
}
