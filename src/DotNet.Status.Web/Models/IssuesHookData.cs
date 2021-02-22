namespace DotNet.Status.Web.Models
{
    public class IssuesHookData
    {
        public string Action { get; set; }
        public Octokit.Issue Issue { get; set; }
        public IssuesHookUser Sender { get; set; }
        public IssuesHookRepository Repository { get; set; }
        public IssuesHookLabel Label { get; set; }
        public IssuesHookChanges Changes { get; set; }
    }
}