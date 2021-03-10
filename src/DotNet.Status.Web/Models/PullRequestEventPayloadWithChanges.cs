using Octokit;

namespace DotNet.Status.Web.Models
{
    public class PullRequestEventPayloadWithChanges : PullRequestEventPayload
    {
        public IssueOrPullRequestCommentChanges Changes { get; set; }
    }
}