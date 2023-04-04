using Microsoft.DotNet.Internal.AzureDevOps;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.DotNet.Internal.DependencyInjection;

namespace Microsoft.DotNet.AzureDevOpsTimeline;

public class BuildLogScraper : IBuildLogScraper
{
    private readonly ILogger<BuildLogScraper> _logger;
    private readonly IClientFactory<IAzureDevOpsClient> _azureDevOpsClientFactory;

    // Example: Environment: windows-latest
    private static readonly Regex _azurePipelinesRegex = new Regex(@"Image: (\S+)");
    // Example: Image: build.ubuntu.1804.amd64
    private static readonly Regex _oneESRegex = new Regex(@"Image: (\S+)");
    // Example: mcr.microsoft.com/dotnet-buildtools/prereqs:centos-7-3e800f1-20190501005343
    private static readonly Regex _dockerImageRegex = new Regex(@"mcr.microsoft.com\/dotnet-buildtools\/prereqs:\S+");

    public BuildLogScraper(ILogger<BuildLogScraper> logger, IClientFactory<IAzureDevOpsClient> azureDevOpsClientFactory)
    {
        _logger = logger;
        _azureDevOpsClientFactory = azureDevOpsClientFactory;
    }

    public Task<string> ExtractMicrosoftHostedPoolImageNameAsync(AzureDevOpsProject project, string logUri, CancellationToken cancellationToken)
        => ExtractImageNameAsync(project, logUri, ExtractMicrosoftHostedImageName, cancellationToken);

    public Task<string> ExtractOneESHostedPoolImageNameAsync(AzureDevOpsProject project, string logUri, CancellationToken cancellationToken)
        => ExtractImageNameAsync(project, logUri, ExtractOneESHostedImageName, cancellationToken);

    public Task<string> ExtractDockerImageNameAsync(AzureDevOpsProject project, string logUri, CancellationToken cancellationToken)
        => ExtractImageNameAsync(project, logUri, TryExtractDockerImageName, cancellationToken);

    private async Task<string> ExtractImageNameAsync(AzureDevOpsProject project, string logUri, Func<string, string> regexFunction, CancellationToken cancellationToken)
    {
        using var clientRef = _azureDevOpsClientFactory.GetClient(project.Organization);
        var client = clientRef.Value;
        var imageName = await client.TryGetImageName(logUri, regexFunction, cancellationToken);

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
