using DotNet.Status.Web.Controllers;
using System.Threading.Tasks;

namespace DotNet.Status.Web
{
    public interface ITimelineIssueTriageService
    {
        Task ProcessIssueEvent(IssuesHookData issuePayload);
    }
}
