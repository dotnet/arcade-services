using Microsoft.DotNet.Internal.AzureDevOps;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.DotNet.Internal.DependencyInjection;
using System.Collections.Generic;

namespace Microsoft.DotNet.AzureDevOpsTimeline;

public class BuildLogScraper : IBuildLogScraper
{
    private readonly ILogger<BuildLogScraper> _logger;
    private readonly IClientFactory<IAzureDevOpsClient> _azureDevOpsClientFactory;

    // Example: Image: windows-latest
    private static readonly Regex _azurePipelinesRegex = new Regex(@"Image: (\S+)");
    // OneES logs need to to match both regexes in order to correctly extract the image name
    // Example: 2023-04-04T15:46:13.8907100Z SKU: Standard_D4a_v4
    //          2023-04-04T15:46:13.8907217Z Image: windows.vs2019.amd64
    // or       2023-04-04T15:10:06.5649938Z SKU: Standard_D4a_v4
    //          2023-04-04T15:10:06.5650033Z Image: 1es-windows-2022
    private static readonly Regex _oneESSkuRegex = new Regex("SKU:.+");
    private static readonly Regex _oneESImageRegex = new Regex(@"Image: (\S+)");
    // Example: mcr.microsoft.com/dotnet-buildtools/prereqs:centos-7-3e800f1-20190501005343
    private static readonly Regex _dockerImageRegex = new Regex(@"(mcr.microsoft.com\/dotnet-buildtools\/prereqs:\S+)");

    public BuildLogScraper(ILogger<BuildLogScraper> logger, IClientFactory<IAzureDevOpsClient> azureDevOpsClientFactory)
    {
        _logger = logger;
        _azureDevOpsClientFactory = azureDevOpsClientFactory;
    }

    public Task<string> ExtractMicrosoftHostedPoolImageNameAsync(AzureDevOpsProject project, string logUri, CancellationToken cancellationToken)
        => ExtractImageNameAsync(project, logUri, new List<Regex>() { _azurePipelinesRegex }, cancellationToken);

    public Task<string> ExtractOneESHostedPoolImageNameAsync(AzureDevOpsProject project, string logUri, CancellationToken cancellationToken)
        => ExtractImageNameAsync(project, logUri, new List<Regex>() { _oneESSkuRegex, _oneESImageRegex }, cancellationToken);

    public Task<string> ExtractDockerImageNameAsync(AzureDevOpsProject project, string logUri, CancellationToken cancellationToken)
        => ExtractImageNameAsync(project, logUri, new List<Regex>() { _dockerImageRegex }, cancellationToken);

    private async Task<string> ExtractImageNameAsync(AzureDevOpsProject project, string logUri, List<Regex> regexes, CancellationToken cancellationToken)
    {
        using var clientRef = _azureDevOpsClientFactory.GetClient(project.Organization);
        var client = clientRef.Value;
        var imageName = await client.MatchLogLineSequence(logUri, regexes, cancellationToken);

        if (imageName == null)
        {
            _logger.LogInformation("Didn't find image name for log `{logUri}`", logUri);
            return null;
        }
            
        return imageName;
    }
}
