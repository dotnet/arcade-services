// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.DotNet.Services.Utility;
using Microsoft.Extensions.Logging;

namespace BuildInsights.Utilities.AzureDevOps;

public class LoggingHandler : AzureDevOpsDelegatingHandler
{
    private readonly ILogger<LoggingHandler> _logger;

    public LoggingHandler(ILogger<LoggingHandler> logger)
        : base(logger)
    {
        _logger = logger;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Beginning azure devops call: {AzureDevOpsUrl}", request.RequestUri?.AbsoluteUri);
        var val = await base.SendAsync(request, cancellationToken);
        _logger.LogInformation(
            "Completed azure devops call: Status {StatusCode}, Size:{ContentSize} url: {AzureDevOpsUrl}",
            val.StatusCode,
            val.Content?.Headers?.ContentLength ?? 0,
            request.RequestUri?.AbsoluteUri);
        return val;
    }
}
