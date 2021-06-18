using System;
using System.Collections.Generic;
using System.Text;
using Octokit;

namespace RolloutScorer.Services
{
    public interface IIssueService
    {
        bool IssueContainsRelevantLabels(Issue issue, string issueLabel, string repoLabel);
    }
}
