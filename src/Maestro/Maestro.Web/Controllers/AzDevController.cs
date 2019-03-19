using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Maestro.AzureDevOps;
using Microsoft.AspNetCore.Mvc;

namespace Maestro.Web.Controllers
{
    [Route("[controller]")]
    [Route("_/[controller]")]
    public class AzDevController : ControllerBase
    {
        private static readonly Lazy<HttpClient> s_lazyClient = new Lazy<HttpClient>(CreateHttpClient);
        private static HttpClient CreateHttpClient() =>
            new HttpClient
            {
                DefaultRequestHeaders =
                {
                    UserAgent =
                    {
                        new ProductInfoHeaderValue("MaestroApi", Helpers.GetApplicationVersion())
                    }
                }
            };

        public IAzureDevOpsTokenProvider TokenProvider { get; }

        public AzDevController(IAzureDevOpsTokenProvider tokenProvider)
        {
            TokenProvider = tokenProvider;
        }

        [HttpGet("build/status/{account}/{project}/{definitionId}/{*branch}")]
        public async Task<IActionResult> GetBuildStatus(string account, string project, int definitionId, string branch, int count)
        {
            string token = await TokenProvider.GetTokenForAccount(account);
            return await HttpContext.ProxyRequestAsync(
                s_lazyClient.Value,
                $"https://dev.azure.com/{account}/{project}/_apis/build/builds?api-version=5.0&definitions={definitionId}&branchName={branch}&statusFilter=completed&$top={count}",
                req =>
                {
                    req.Headers.Authorization = new AuthenticationHeaderValue(
                        "Basic",
                        Convert.ToBase64String(Encoding.UTF8.GetBytes(":" + token)));
                });
        }
    }
}
