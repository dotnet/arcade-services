using Castle.Core.Logging;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Microsoft.DotNet.AzureDevOpsTimeline
{
    internal class BuildLogScraper : IBuildLogScraper
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<BuildLogScraper> _logger;

        public BuildLogScraper(ILogger<BuildLogScraper> logger)
        {
            _httpClient = GetHttpClient(Environment.GetEnvironmentVariable("AzdoToken"));
            _logger = logger;
        }

        public Task<string> ExtractMicrosoftHostedPoolImageNameAsync(string logUri)
            => ExtractImageNameAsync(logUri, azurePipelinesRegex);

        public Task<string> ExtractOneESHostedPoolImageNameAsync(string logUri)
            => ExtractImageNameAsync(logUri, oneESRegex);

        private async Task<string> ExtractImageNameAsync(string logUri, Regex imageNameRegex)
        {
            if (string.IsNullOrEmpty(logUri))
            {
                throw new ArgumentException("Log URI can't be empty", nameof(logUri));
            }

            string logText = await TryGetLogContents(logUri);
            if (string.IsNullOrEmpty(logText))
            {
                _logger.LogWarning($"Got empty log file for '{logUri}'");
                return string.Empty;
            }

            Match match = imageNameRegex.Match(logText);
            if (match.Success)
            {
                return match.Groups[1].Value;
            }
            else
            {
                _logger.LogWarning($"No matches for regex '{imageNameRegex}' in log {logUri}");
                return string.Empty;
            }
        }

        private async Task<string> TryGetLogContents(string logUri)
        {
            try
            {
                return await _httpClient.GetStringAsync(logUri);
            }
            catch (Exception exception)
            {
                _logger.LogWarning($"Exception thrown when trying to get log '{logUri}': {exception}");
                return string.Empty;
            }
        }

        private static readonly Regex azurePipelinesRegex = new Regex(@"Environment: (\S+)");
        private static readonly Regex oneESRegex = new Regex(@"Image: (\S+)");

        private HttpClient GetHttpClient(string azureDevOpsAccessToken)
        {
            var httpClient = new HttpClient(new HttpClientHandler { CheckCertificateRevocationList = true });
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Basic",
                Convert.ToBase64String(Encoding.ASCII.GetBytes($":{azureDevOpsAccessToken}")));
            return httpClient;
        }
    }
}
