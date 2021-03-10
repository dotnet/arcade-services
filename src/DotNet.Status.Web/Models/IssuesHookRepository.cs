namespace DotNet.Status.Web.Models
{
    public class IssuesHookRepository
    {
        public string Name { get; set; }
        public IssuesHookUser Owner { get; set; }
        public long Id { get; set; }
    }
}