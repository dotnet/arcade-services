using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.DotNet.Internal.AzureDevOps;

namespace Microsoft.DotNet.AzureDevOpsTimeline
{
    public interface IBuildLogScraper
    {
        Task<string> ExtractMicrosoftHostedPoolImageNameAsync(IAzureDevOpsClient client, string logUri, CancellationToken cancellationToken);
        Task<string> ExtractOneESHostedPoolImageNameAsync(IAzureDevOpsClient client, string logUri, CancellationToken cancellationToken);
        Task<string> ExtractDockerImageNameAsync(IAzureDevOpsClient client, string logUri, CancellationToken cancellationToken);
    }
}
