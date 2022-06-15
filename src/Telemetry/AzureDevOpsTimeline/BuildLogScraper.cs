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

        private static readonly Regex _azurePipelinesRegex = new Regex(@"Environment: (\S+)");
        private static readonly Regex _oneESRegex = new Regex(@"Image: (\S+)");

        public BuildLogScraper(ILogger<BuildLogScraper> logger, IAzureDevOpsClient client)
        {
            _azureDevOpsClient = client;
            _logger = logger;
        }

        public Task<string> ExtractMicrosoftHostedPoolImageNameAsync(string logUri, CancellationToken cancellationToken)
            => ExtractImageNameAsync(logUri, _azurePipelinesRegex, cancellationToken);

        public Task<string> ExtractOneESHostedPoolImageNameAsync(string logUri, CancellationToken cancellationToken)
            => ExtractImageNameAsync(logUri, _oneESRegex, cancellationToken);

        private async Task<string> ExtractImageNameAsync(string logUri, Regex imageNameRegex, CancellationToken cancellationToken)
        {
            var imageName = await _azureDevOpsClient.TryGetImageName(logUri, imageNameRegex, _logger, cancellationToken);

            if (imageName == null)
            {
                _logger.LogWarning($"Didn't find image name for log `{logUri}`");
                return null;
            }
            
            return imageName;
        }
    }
}
