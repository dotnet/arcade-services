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
        var retriesRemaining = retryCount;
        // Add a bit of randomness to the retry delay.
        var rng = new Random();

        HttpStatusCode[] stopRetriesHttpStatusCodes = [
            HttpStatusCode.NotFound,
            HttpStatusCode.UnprocessableEntity,
            HttpStatusCode.BadRequest,
            HttpStatusCode.Unauthorized,
            HttpStatusCode.Forbidden ];

        while (true)
        {
            HttpResponseMessage response = null;

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
                                "A '{httpCode} - {status}' status was returned for a HTTP request. We'll set the retries amount to 0. {error}",
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
                        _logger.LogError("There was an error executing method '{method}' against URI '{requestUri}' " +
                                         "after {maxRetries} attempts. Exception: {exception}",
                                         _method,
                                         _requestUri,
                                         retryCount,
                                         ex);
                    }
                    throw;
                }
                else if (_logFailure)
                {
                    _logger.LogWarning("There was an error executing method '{method}' against URI '{requestUri}'. " +
                                       "{retriesRemaining} attempts remaining. Exception: {ex.ToString()}",
                                       _method,
                                       _requestUri,
                                       retriesRemaining,
                                       ex);
                }
            }
            --retriesRemaining;
            var delay = (retryCount - retriesRemaining) * rng.Next(1, 7);
            await Task.Delay(delay * 1000);
        }
    }
}
