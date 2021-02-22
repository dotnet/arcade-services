using Octokit;

namespace DotNet.Status.Web.Models
{
    public class PullRequestCommentPayloadWithChanges : PullRequestCommentPayload
    {
        public IssueOrPullRequestCommentChanges Changes { get; set; }
    }
}