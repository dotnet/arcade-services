using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Microsoft.DotNet.AzureDevOpsTimeline
{
    public interface IBuildLogScraper
    {
        Task<string> ExtractMicrosoftHostedPoolImageNameAsync(string logUri);
        Task<string> ExtractOneESHostedPoolImageNameAsync(string logUri);
    }
}
