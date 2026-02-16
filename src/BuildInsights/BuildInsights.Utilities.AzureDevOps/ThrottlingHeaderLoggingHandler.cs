// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.ApplicationInsights;
using Microsoft.DotNet.Services.Utility;
using Microsoft.Extensions.Internal;
using Microsoft.Extensions.Logging;

namespace BuildInsights.Utilities.AzureDevOps;

public class ThrottlingHeaderLoggingHandler : AzureDevOpsDelegatingHandler
{
    private readonly ISystemClock _clock;
    private readonly TelemetryClient _telemetry;
    private readonly ILogger<ThrottlingHeaderLoggingHandler> _logger;

    public ThrottlingHeaderLoggingHandler(
        ISystemClock clock,
        TelemetryClient telemetry,
        ILogger<ThrottlingHeaderLoggingHandler> logger) : base(logger)
    {
        _clock = clock;
        _telemetry = telemetry;
        _logger = logger;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        HttpResponseMessage response = await base.SendAsync(request, cancellationToken);

        string limitedResource = GetSingleHeader(response, "X-RateLimit-Resource") ?? "<<unknown>>";

        string delay = GetSingleHeader(response, "X-RateLimit-Delay");
        if (delay != null)
        {
            if (!double.TryParse(delay, out double delayValue))
            {
                _logger.LogError("X-RateLimit-Delay is not a valid double: '{value}'", delay);
            }
            else
            {
                _telemetry.TrackMetric(
                    "AzureDevOpsResourceDelay",
                    delayValue,
                    new Dictionary<string, string>
                    {
                        {"Resource", limitedResource},
                    }
                );
            }
        }

        string remaining = GetSingleHeader(response, "X-RateLimit-Remaining");
        string limit = GetSingleHeader(response, "X-RateLimit-Limit");

        if (remaining != null)
        {
            if (!double.TryParse(remaining, out double remainingValue))
            {
                _logger.LogError("X-RateLimit-Remaining is not a valid double: '{value}'", remaining);
            }
            else if (!double.TryParse(limit, out double limitValue))
            {
                _logger.LogError("X-RateLimit-Limit is not a valid double: '{value}'", limit);
            }
            else
            {
                var metrics = new Dictionary<string, double>
                {
                    {"Remaining", remainingValue},
                    {"Limit", limitValue}
                };

                string reset = GetSingleHeader(response, "X-RateLimit-Reset");
                if (reset != null)
                {
                    if (!long.TryParse(reset, out long resetValue))
                    {
                        _logger.LogError("X-RateLimit-Reset is not a valid long: '{value}'", reset);
                    }
                    else
                    {
                        DateTimeOffset now = _clock.UtcNow;
                        DateTimeOffset resetTime = DateTimeOffset.FromUnixTimeSeconds(resetValue);
                        if (resetTime < now.Subtract(TimeSpan.FromHours(1)))
                        {
                            _logger.LogError(
                                "X-RateLimit-Reset ({RateLimitReset}) appears to represent a time significantly in the past: {date}",
                                reset,
                                resetTime
                            );
                        }
                        else
                        {
                            metrics.Add("SecondsToReset", (now - resetTime).TotalSeconds);
                        }
                    }
                }

                _telemetry.TrackEvent(
                    "AzureDevOpsResourceLimit",
                    new Dictionary<string, string>
                    {
                        {"Resource", limitedResource},
                    },
                    metrics
                );
            }
        }
        else if (limit != null)
        {
            _logger.LogError("X-RateLimit-Limit present without X-RateLimit-Remaining");
        }

        return response;
    }
}
