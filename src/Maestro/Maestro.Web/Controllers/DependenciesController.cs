using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.HealthMetrics;
using Microsoft.Extensions.Logging;

namespace Maestro.Web.Controllers
{
    [Route("[controller]")]
    [Route("_/[controller]")]
    public class DependenciesController : ControllerBase
    {
        private readonly IRemoteFactory remoteFactory;
        private readonly ILogger<DependenciesController> logger;

        public DependenciesController(IRemoteFactory factory, ILogger<DependenciesController> logger)
        {
            remoteFactory = factory;
            this.logger = logger;
        }

        [HttpGet("getSubscriptionDependencyDetails/{host}/{account}/{project}/{*branch}")]
        [AllowAnonymous]
        public async Task<IActionResult> GetSubscriptionDependencyDetails(string host, string account, string project, string branch)
        {
            string repo = "";

            if (host == "github")
            {
                repo = $"https://github.com/{account}/{project}";
            }
            else
            {
                repo = $"https://dev.azure.com/{account}/internal/_git/{project}";
            }

            SubscriptionHealthMetric healthMetric = new SubscriptionHealthMetric(repo, branch, d => true, logger, remoteFactory);
            await healthMetric.EvaluateAsync();
            string breakHere = healthMetric.Branch;
            var temp = new SubscriptionDependencyDetails(healthMetric);
            return Ok(new SubscriptionDependencyDetails(healthMetric));
        }
    }
}
