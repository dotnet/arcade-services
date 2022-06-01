using Microsoft.Extensions.Internal;
using Microsoft.Extensions.Logging;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.DotNet.Services.Utility
{
    public class RetryAfterHandler : AzureDevOpsDelegatingHandler
    {
        private readonly ISystemClock _clock;
        private readonly ILogger<RetryAfterHandler> _logger;

        public RetryAfterHandler(
            ISystemClock clock,
            ILogger<RetryAfterHandler> logger) : base(logger)
        {
            _clock = clock;
            _logger = logger;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            HttpResponseMessage response = await base.SendAsync(request, cancellationToken);
            RetryConditionHeaderValue retryAfter = response.Headers.RetryAfter;

            if (retryAfter == null)
                return response;

            if (retryAfter.Date.HasValue)
            {
                response.Dispose();
                _logger.LogWarning("Retry-After detected, delaying until {date}", retryAfter.Date.Value);
                await Task.Delay(_clock.UtcNow - retryAfter.Date.Value, cancellationToken);
                return await base.SendAsync(request, cancellationToken);
            }

            if (retryAfter.Delta.HasValue)
            {
                response.Dispose();
                _logger.LogWarning("Retry-After detected, delaying for {delta}", retryAfter.Delta.Value);
                await Task.Delay(retryAfter.Delta.Value, cancellationToken);
                return await base.SendAsync(request, cancellationToken);
            }

            _logger.LogError("Retry after found with no date/delta");
            return response;
        }
    }
}
