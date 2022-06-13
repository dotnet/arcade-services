using Microsoft.DotNet.Internal.AzureDevOps;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Net.Http;
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
            var imageName = await _azureDevOpsClient.TryGetImageName(logUri, imageNameRegex, LogException, cancellationToken);

            if (imageName == string.Empty)
            {
                _logger.LogWarning($"Didn't find image name for log {logUri}");
                return null;
            }
            
            return imageName;
        }

        private void LogException(Exception ex)
        {
            _logger.LogWarning($"Exception thrown during getting the log {ex.Message}, retrying");
        }

        private static readonly Regex azurePipelinesRegex = new Regex(@"Environment: (\S+)");
        private static readonly Regex oneESRegex = new Regex(@"Image: (\S+)");
    }
}
