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
using System.Threading;
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

        public Task<string> ExtractMicrosoftHostedPoolImageNameAsync(string logUri, CancellationToken cancellationToken)
            => ExtractImageNameAsync(logUri, azurePipelinesRegex, cancellationToken);

        public Task<string> ExtractOneESHostedPoolImageNameAsync(string logUri, CancellationToken cancellationToken)
            => ExtractImageNameAsync(logUri, oneESRegex, cancellationToken);

        private async Task<string> ExtractImageNameAsync(string logUri, Regex imageNameRegex, CancellationToken cancellationToken)
        {
            string logText = await _azureDevOpsClient.TryGetLogContents(logUri, cancellationToken);

            if (string.IsNullOrEmpty(logText))
            {
                _logger.LogInformation($"Got empty log file for '{logUri}'");
                return null;
            }

            Match match = imageNameRegex.Match(logText);
            if (match.Success)
            {
                return match.Groups[1].Value;
            }
            else
            {
                _logger.LogWarning($"No matches for regex '{imageNameRegex}' in log {logUri}");
                return null;
            }
        }

        private static readonly Regex azurePipelinesRegex = new Regex(@"Environment: (\S+)");
        private static readonly Regex oneESRegex = new Regex(@"Image: (\S+)");
    }
}
