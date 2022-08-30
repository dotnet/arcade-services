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

        // Example: Environment: windows-latest
        private static readonly Regex _azurePipelinesRegex = new Regex(@"Environment: (\S+)");
        // Example: Image: build.ubuntu.1804.amd64
        private static readonly Regex _oneESRegex = new Regex(@"Image: (\S+)");
        // Example: mcr.microsoft.com/dotnet-buildtools/prereqs:centos-7-3e800f1-20190501005343
        private static readonly Regex _dockerImageRegex = new Regex(@"mcr.microsoft.com\/dotnet-buildtools\/prereqs:\S+");

        public BuildLogScraper(ILogger<BuildLogScraper> logger, IAzureDevOpsClient client)
        {
            _azureDevOpsClient = client;
            _logger = logger;
        }

        public Task<string> ExtractMicrosoftHostedPoolImageNameAsync(string logUri, CancellationToken cancellationToken)
            => ExtractImageNameAsync(logUri, ExtractMicrosoftHostedImageName, cancellationToken);

        public Task<string> ExtractOneESHostedPoolImageNameAsync(string logUri, CancellationToken cancellationToken)
            => ExtractImageNameAsync(logUri, ExtractOneESHostedImageName, cancellationToken);

        public Task<string> ExtractDockerImageNameAsync(string logUri, CancellationToken cancellationToken)
            => ExtractImageNameAsync(logUri, TryExtractDockerImageName, cancellationToken);

        private async Task<string> ExtractImageNameAsync(string logUri, Func<string, string> regexFunction, CancellationToken cancellationToken)
        {
            var imageName = await _azureDevOpsClient.TryGetImageName(logUri, regexFunction, cancellationToken);

            if (imageName == null)
            {
                _logger.LogInformation("Didn't find image name for log `{logUri}`", logUri);
                return null;
            }
            
            return imageName;
        }

        private string ExtractOneESHostedImageName(string line)
        {
            var match = _oneESRegex.Match(line);
            if (match.Success)
            {
                return match.Groups[1].Value;
            }

            return null;
        }

        private string ExtractMicrosoftHostedImageName(string line)
        {
            var match = _azurePipelinesRegex.Match(line);
            if (match.Success)
            {
                return match.Groups[1].Value;
            }

            return null;
        }

        private string TryExtractDockerImageName(string line)
        {
            var match = _dockerImageRegex.Match(line);
            if (match.Success)
            {
                return match.Value;
            }

            return null;
        }
    }
}
