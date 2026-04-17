// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using BuildInsights.ServiceDefaults;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

await VpnCheckCommand.RunAsync();

internal static class VpnCheckCommand
{
    public static async Task RunAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development,
        });

        builder.AddSharedConfiguration();
        builder.Services.AddSingleton<VpnCheckState>();
        builder.Services.AddHttpClient("VpnCheck", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(5);
        });
        builder.Services.AddHostedService<VpnConnectivityMonitor>();

        await using var app = builder.Build();

        app.MapGet("/health", (VpnCheckState state) =>
            state.IsReady
                ? Results.Ok(new
                {
                    status = "Healthy",
                    phase = state.Phase,
                    target = state.Target,
                })
                : Results.Json(
                    new
                    {
                        status = state.LastError is null ? "Starting" : "Unhealthy",
                        phase = state.Phase,
                        target = state.Target,
                        lastError = state.LastError,
                    },
                    statusCode: StatusCodes.Status503ServiceUnavailable));

        await app.RunAsync();
    }

    private sealed class VpnConnectivityMonitor : BackgroundService
    {
        private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan HealthyRecheckDelay = TimeSpan.FromMinutes(1);

        private readonly IConfiguration _configuration;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly VpnCheckState _state;
        private readonly ILogger<VpnConnectivityMonitor> _logger;

        public VpnConnectivityMonitor(
            IConfiguration configuration,
            IHttpClientFactory httpClientFactory,
            VpnCheckState state,
            ILogger<VpnConnectivityMonitor> logger)
        {
            _configuration = configuration;
            _httpClientFactory = httpClientFactory;
            _state = state;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var kustoClusterUriValue = _configuration.GetValue<string>($"{BuildInsightsCommonConfiguration.ConfigurationKeys.Kusto}:KustoClusterUri")
                ?? throw new InvalidOperationException("Kusto:KustoClusterUri is not configured.");

            if (!Uri.TryCreate(kustoClusterUriValue, UriKind.Absolute, out var kustoClusterUri))
            {
                throw new InvalidOperationException($"Kusto:KustoClusterUri '{kustoClusterUriValue}' is not a valid absolute URI.");
            }

            _state.SetTarget(kustoClusterUri.AbsoluteUri);
            _logger.LogInformation("Starting VPN connectivity monitor for {KustoClusterUri}", kustoClusterUri);

            while (!stoppingToken.IsCancellationRequested)
            {
                await ProbeAsync(kustoClusterUri, stoppingToken);

                var delay = _state.IsReady ? HealthyRecheckDelay : RetryDelay;

                try
                {
                    await Task.Delay(delay, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
            }
        }

        private async Task ProbeAsync(Uri kustoClusterUri, CancellationToken cancellationToken)
        {
            if (!_state.IsReady)
            {
                _state.ReportProgress("Testing VPN connection");
            }

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Head, kustoClusterUri);
                using var response = await _httpClientFactory.CreateClient("VpnCheck")
                    .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

                _state.ReportReady($"VPN check succeeded ({(int)response.StatusCode} {response.ReasonPhrase})");
                _logger.LogInformation(
                    "VPN connectivity probe to {KustoClusterUri} succeeded with status code {StatusCode}",
                    kustoClusterUri,
                    (int)response.StatusCode);
            }
            catch (Exception ex)
            {
                if (ex is OperationCanceledException && cancellationToken.IsCancellationRequested)
                {
                    throw;
                }

                _state.ReportFailure("VPN check failed", ex.Message);
                _logger.LogWarning(ex, "VPN connectivity probe to {KustoClusterUri} failed", kustoClusterUri);
            }
        }
    }

    private sealed class VpnCheckState
    {
        private readonly object _lock = new();
        private string _phase = "Starting up";
        private string? _target;
        private string? _lastError;
        private bool _isReady;

        public string Phase
        {
            get
            {
                lock (_lock)
                {
                    return _phase;
                }
            }
        }

        public string? Target
        {
            get
            {
                lock (_lock)
                {
                    return _target;
                }
            }
        }

        public string? LastError
        {
            get
            {
                lock (_lock)
                {
                    return _lastError;
                }
            }
        }

        public bool IsReady
        {
            get
            {
                lock (_lock)
                {
                    return _isReady;
                }
            }
        }

        public void SetTarget(string target)
        {
            lock (_lock)
            {
                _target = target;
            }
        }

        public void ReportProgress(string phase)
        {
            lock (_lock)
            {
                _phase = phase;
                _isReady = false;
            }
        }

        public void ReportReady(string phase)
        {
            lock (_lock)
            {
                _phase = phase;
                _lastError = null;
                _isReady = true;
            }
        }

        public void ReportFailure(string phase, string error)
        {
            lock (_lock)
            {
                _phase = phase;
                _lastError = error;
                _isReady = false;
            }
        }
    }
}
