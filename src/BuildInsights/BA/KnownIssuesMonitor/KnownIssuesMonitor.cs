using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.DotNet.ServiceFabric.ServiceHost;
using Microsoft.Internal.Helix.KnownIssues.Services;
using Microsoft.Internal.Helix.Utility;

namespace Microsoft.Internal.Helix.KnownIssuesMonitor
{
    [DependencyInjected]
    public class KnownIssuesMonitor : IServiceImplementation
    {
        private readonly IKnownIssueReporter _issueReporter;
        public KnownIssuesMonitor(IKnownIssueReporter issueReporter)
        {
            _issueReporter = issueReporter;
        }

        [CronSchedule("0 0 * ? * * *", TimeZones.PST)] //Every day every hour
        public async Task ExecuteKnownIssueReporter(CancellationToken cancellationToken)
        {
            await _issueReporter.ExecuteKnownIssueReporter();
        }

        public Task<TimeSpan> RunAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(TimeSpan.MaxValue);
        }
    }
}
