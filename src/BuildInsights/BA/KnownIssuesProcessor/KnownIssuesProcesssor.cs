using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.DotNet.ServiceFabric.ServiceHost;
using Microsoft.Internal.Helix.Utility;
using Microsoft.Internal.Helix.Utility.Parallel;

namespace Microsoft.Internal.Helix.KnownIssuesProcessor
{
    [DependencyInjected]
    public class KnownIssuesProcessor : IServiceImplementation
    {
        private readonly ThreadRunner _threadRunner;

        public KnownIssuesProcessor(ThreadRunner threadRunner)
        {
            _threadRunner = threadRunner;
        }

        public async Task<TimeSpan> RunAsync(CancellationToken cancellationToken)
        {
            await _threadRunner.RunAsync(cancellationToken);
            return TimeSpan.Zero;
        }
    }
}
