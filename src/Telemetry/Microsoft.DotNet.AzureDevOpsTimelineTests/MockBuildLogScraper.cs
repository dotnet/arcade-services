using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.DotNet.AzureDevOpsTimeline.Tests
{
    public class MockBuildLogScraper : IBuildLogScraper
    {
        public Task<string> ExtractMicrosoftHostedPoolImageNameAsync(string logUri, CancellationToken cancellationToken)
        {
            return Task.FromResult("");
        }

        public Task<string> ExtractOneESHostedPoolImageNameAsync(string logUri, CancellationToken cancellationToken)
        {
            return Task.FromResult("");
        }
    }
}
