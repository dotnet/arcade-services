using Microsoft.Extensions.Logging;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.DotNet.Services.Utility
{
    public class RetryAllHandler : DelegatingHandler
    {
        private readonly ILogger<RetryAllHandler> _logger;
        private readonly ExponentialRetry _retry;

        public RetryAllHandler(ILogger<RetryAllHandler> logger, ExponentialRetry retry)
        {
            _logger = logger;
            _retry = retry;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return await _retry.RetryAsync(async cancellationToken =>
            {
                var response = await base.SendAsync(request, cancellationToken);

                response.EnsureSuccessStatusCode();

                return response;
            },
            ex => _logger.LogWarning("Exception thrown during getting the log `{exception}`, retrying", ex),
            _ => true,
            cancellationToken);
        }
    }
}
