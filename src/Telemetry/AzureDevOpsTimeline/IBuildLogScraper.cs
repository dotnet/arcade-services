using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.DotNet.AzureDevOpsTimeline
{
    public interface IBuildLogScraper
    {
        Task<string> ExtractMicrosoftHostedPoolImageNameAsync(string logUri, CancellationToken cancellationToken);
        Task<string> ExtractOneESHostedPoolImageNameAsync(string logUri, CancellationToken cancellationToken);
        Task<string> ExtractDockerImageNameAsync(string logUri, CancellationToken cancellationToken);
    }
}
