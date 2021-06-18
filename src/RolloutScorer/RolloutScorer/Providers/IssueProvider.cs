using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Extensions.Logging;
using Octokit;
using RolloutScorer.Services;

namespace RolloutScorer.Providers
{
    public class IssueProvider : IIssueService
    {
        private readonly ILogger<IssueProvider> _logger;

        public IssueProvider(ILogger<IssueProvider> logger)
        {
            _logger = logger;
        }

        public bool IssueContainsRelevantLabels(Issue issue, string issueLabel, string repoLabel)
        {
            if (issue == null)
            {
                _logger.LogWarning("A null issue was passed.");
                return false;
            }

            _logger.LogInformation($"Issue {issue.Number} has labels {string.Join(", ", issue.Labels.Select(l => $"'{l.Name}'"))}");

            bool isIssueLabel = false;

            if (issueLabel == GithubLabelNames.IssueLabel)
            {
                isIssueLabel = issue.Labels.Any(l => l.Name == repoLabel)
                    && !issue.Labels.Any(l => l.Name == GithubLabelNames.HotfixLabel ||
                    l.Name == GithubLabelNames.RollbackLabel ||
                    l.Name == GithubLabelNames.DowntimeLabel ||
                    l.Name == GithubLabelNames.FailureLabel);
            }
            else
            {
                isIssueLabel = issue.Labels.Any(l => l.Name == issueLabel) && issue.Labels.Any(l => l.Name == repoLabel);
            }

            if (isIssueLabel)
            {
                _logger.LogInformation($"Issue {issue.Number} determined to be {issueLabel} for {repoLabel}");
            }

            return isIssueLabel;
        }
    }
}
