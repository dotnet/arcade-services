// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Extensions.Internal;
using Microsoft.Extensions.Logging;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.ApplicationInsights;

namespace Microsoft.DotNet.Services.Utility
{
    public class RetryAfterHandler : AzureDevOpsDelegatingHandler
    {
        private readonly ISystemClock _clock;
        private readonly ILogger<RetryAfterHandler> _logger;
        private readonly TelemetryClient _telemetry;

        public RetryAfterHandler(
            ISystemClock clock,
            ILogger<RetryAfterHandler> logger,
            TelemetryClient telemetry = null) : base(logger)
        {
            _clock = clock;
            _logger = logger;
            _telemetry = telemetry;
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
                _telemetry?.TrackEvent("AzureDevOpsThrottled");
                _logger.LogWarning("Retry-After detected, delaying until {date}", retryAfter.Date.Value);
                await Task.Delay(_clock.UtcNow - retryAfter.Date.Value, cancellationToken);
                return await base.SendAsync(request, cancellationToken);
            }

            if (retryAfter.Delta.HasValue)
            {
                response.Dispose();
                _telemetry?.TrackEvent("AzureDevOpsThrottled");
                _logger.LogWarning("Retry-After detected, delaying for {delta}", retryAfter.Delta.Value);
                await Task.Delay(retryAfter.Delta.Value, cancellationToken);
                return await base.SendAsync(request, cancellationToken);
            }

            _logger.LogError("Retry after found with no date/delta");
            return response;
        }
    }
}
