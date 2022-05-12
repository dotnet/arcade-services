using Castle.Core.Logging;
using Microsoft.DotNet.Internal.AzureDevOps;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
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
    public class BuildLogScraper : IBuildLogScraper
    {
        private readonly ILogger<BuildLogScraper> _logger;
        private readonly IAzureDevOpsClient _azureDevOpsClient;

        public BuildLogScraper(ILogger<BuildLogScraper> logger, IAzureDevOpsClient client)
        {
            _azureDevOpsClient = client;
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

            string logText;
            try
            {
                logText = await _azureDevOpsClient.TryGetLogContents(logUri);
            }
            catch(Exception exception)
            {
                _logger.LogWarning($"Exception thrown when trying to get log '{logUri}': {exception}");
                return string.Empty;
            }

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

        private static readonly Regex azurePipelinesRegex = new Regex(@"Environment: (\S+)");
        private static readonly Regex oneESRegex = new Regex(@"Image: (\S+)");
    }
}
