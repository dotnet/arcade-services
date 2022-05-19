using Microsoft.DotNet.Internal.AzureDevOps;
using Microsoft.Extensions.Logging;
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
            using HttpResponseMessage response = await _azureDevOpsClient.TryGetLogContents(logUri, cancellationToken);
            using Stream logStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using StreamReader reader = new StreamReader(logStream);
            while (!reader.EndOfStream)
            {
                var line = reader.ReadLine();
                
                Match match = imageNameRegex.Match(line);
                if (match.Success)
                {
                    return match.Groups[1].Value;
                }
            }

            _logger.LogWarning($"Didn't find image name for log {logUri}");
            return null;
        }

        private static readonly Regex azurePipelinesRegex = new Regex(@"Environment: (\S+)");
        private static readonly Regex oneESRegex = new Regex(@"Image: (\S+)");
    }
}
