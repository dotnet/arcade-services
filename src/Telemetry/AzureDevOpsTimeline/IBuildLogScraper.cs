using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.DotNet.Internal.AzureDevOps;

namespace Microsoft.DotNet.AzureDevOpsTimeline;

public interface IBuildLogScraper
{
    Task<string> ExtractMicrosoftHostedPoolImageNameAsync(AzureDevOpsProject project, string logUri, CancellationToken cancellationToken);
    Task<string> ExtractOneESHostedPoolImageNameAsync(AzureDevOpsProject project, string logUri, CancellationToken cancellationToken);
    Task<string> ExtractDockerImageNameAsync(AzureDevOpsProject project, string logUri, CancellationToken cancellationToken);
}
