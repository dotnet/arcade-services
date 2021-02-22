using Octokit;

namespace DotNet.Status.Web.Models
{
    public class IssueCommentPayloadWithChanges : IssueCommentPayload
    {
        public IssueOrPullRequestCommentChanges Changes { get; set; }

    }
}