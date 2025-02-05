// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Microsoft.DotNet.DarcLib.Helpers;

public class HttpRequestManager
{
    private const string GitHubRateLimitRemainingHeader = "x-ratelimit-remaining";
    private const string GitHubRateLimitResetHeader = "x-ratelimit-reset";

    private readonly HttpClient _client;
    private readonly ILogger _logger;
    private readonly bool _logFailure;
    private readonly string _body;
    private readonly string _requestUri;
    private readonly AuthenticationHeaderValue _authHeader;
    private readonly Action<HttpRequestMessage> _configureRequestMessage;
    private readonly HttpMethod _method;
    private readonly HttpCompletionOption _httpCompletionOption;

    public HttpRequestManager(
        HttpClient client,
        HttpMethod method,
        string requestUri,
        ILogger logger,
        string body = null,
        string versionOverride = null,
        bool logFailure = true,
        AuthenticationHeaderValue authHeader = null,
        Action<HttpRequestMessage> configureRequestMessage = null,
        HttpCompletionOption httpCompletionOption = HttpCompletionOption.ResponseContentRead)
    {
        _client = client;
        _logger = logger;
        _logFailure = logFailure;
        _body = body;
        _requestUri = requestUri;
        _method = method;
        _authHeader = authHeader;
        _configureRequestMessage = configureRequestMessage;
        _httpCompletionOption = httpCompletionOption;
    }

    public async Task<HttpResponseMessage> ExecuteAsync(int retryCount = 3)
    {
        var retriesRemaining = retryCount + 1;
        var attempts = 0;
        // Add a bit of randomness to the retry delay.
        var rng = new Random();

        HttpStatusCode[] stopRetriesHttpStatusCodes =
        [
            HttpStatusCode.NotFound,
            HttpStatusCode.UnprocessableEntity,
            HttpStatusCode.BadRequest,
            HttpStatusCode.Unauthorized,
            HttpStatusCode.Forbidden
        ];

        while (true)
        {
            retriesRemaining--;
            HttpResponseMessage response = null;
            var delay = TimeSpan.FromSeconds((retryCount - retriesRemaining) * rng.Next(1, 7));
            attempts++;

            try
            {
                using (var message = new HttpRequestMessage(_method, _requestUri))
                {
                    if (!string.IsNullOrEmpty(_body))
                    {
                        message.Content = new StringContent(_body, Encoding.UTF8, "application/json");
                    }

                    if (_authHeader != null)
                    {
                        message.Headers.Authorization = _authHeader;
                    }

                    _configureRequestMessage?.Invoke(message);

                    response = await _client.SendAsync(message, _httpCompletionOption);

                    // Handle GitHub rate limiting
                    if (response.StatusCode == HttpStatusCode.Forbidden
                        && response.Headers.TryGetValues(GitHubRateLimitRemainingHeader, out var values)
                        && values.FirstOrDefault() == "0")
                    {
                        if (retriesRemaining <= 0)
                        {
                            throw new HttpRequestException("GitHub rate limit hit");
                        }

                        if (response.Headers.TryGetValues(GitHubRateLimitResetHeader, out var resetValues)
                            && long.TryParse(resetValues.FirstOrDefault(), out long resetSeconds))
                        {
                            var resetTime = DateTimeOffset.FromUnixTimeSeconds(resetSeconds);
                            var delayTime = resetTime - DateTimeOffset.UtcNow;
                            if (delayTime.TotalSeconds > 0)
                            {
                                delay = delayTime;
                            }
                            else
                            {
                                // GitHub documentation says to default to 1 minute when no reset header is supplied:
                                // https://docs.github.com/en/rest/using-the-rest-api/rate-limits-for-the-rest-api?apiVersion=2022-11-28#exceeding-the-rate-limit
                                delay = TimeSpan.FromSeconds(60);
                            }
                        }
                        else
                        {
                            // GitHub documentation says to default to 1 minute when no reset header is supplied:
                            // https://docs.github.com/en/rest/using-the-rest-api/rate-limits-for-the-rest-api?apiVersion=2022-11-28#exceeding-the-rate-limit
                            delay = TimeSpan.FromSeconds(60);
                        }


                        _logger.LogWarning("GitHub rate limit hit. Waiting for {reset} seconds...", delay.TotalSeconds);
                        await Task.Delay(delay);
                        continue;
                    }

                    if (stopRetriesHttpStatusCodes.Contains(response.StatusCode))
                    {
                        if (_logFailure)
                        {
                            var errorDetails = await response.Content.ReadAsStringAsync();

                            if (!string.IsNullOrEmpty(errorDetails))
                            {
                                errorDetails = $"Error details: {errorDetails}";
                            }

                            _logger.LogError(
                                "Non-continuable status '{httpCode} - {status}' was returned for a HTTP request. {error}",
                                (int)response.StatusCode,
                                response.StatusCode,
                                errorDetails);
                        }

                        retriesRemaining = 0;
                    }

                    response.EnsureSuccessStatusCode();
                    return response;
                }
            }
            catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException)
            {
                response?.Dispose();

                // For CLI users this will look normal, but translating to a DarcAuthenticationFailureException means it opts in to automated failure logging.
                if (ex is HttpRequestException && ex.Message.Contains(((int)HttpStatusCode.Unauthorized).ToString()))
                {
                    var queryParamIndex = _requestUri.IndexOf('?');
                    var sanitizedRequestUri = queryParamIndex < 0 ? _requestUri : $"{_requestUri.Substring(0, queryParamIndex)}?***";
                    _logger.LogError(ex, "Non-continuable HTTP 401 error encountered while making request against URI '{sanitizedRequestUri}'", sanitizedRequestUri);
                    throw new DarcAuthenticationFailureException($"Failure to authenticate: {ex.Message}");
                }

                if (retriesRemaining <= 0)
                {
                    if (_logFailure)
                    {
                        _logger.LogError(
                            "Executing HTTP {method} againt '{requestUri}' failed after {attempts} attempts. Exception: {exception}",
                            _method,
                            _requestUri,
                            attempts,
                            ex);
                    }
                    throw;
                }

                if (_logFailure)
                {
                    _logger.LogWarning(
                        "HTTP {method} against '{requestUri}' failed. {retriesRemaining} attempts remaining. " +
                        "Will retry in {retryDelay} seconds. Exception: {exception}",
                        _method,
                        _requestUri,
                        retriesRemaining,
                        delay,
                        ex);
                }

                await Task.Delay(delay);
            }
        }
    }
}
