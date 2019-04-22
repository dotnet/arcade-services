// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Microsoft.DotNet.DarcLib
{
    public class HttpRequestManager
    {
        private HttpClient _client;
        private readonly ILogger _logger;
        private readonly bool _logFailure;
        private readonly string _body;
        private readonly string _requestUri;
        private readonly HttpMethod _method;

        public HttpRequestManager(
            HttpClient client,
            HttpMethod method,
            string requestUri,
            ILogger logger,
            string body = null,
            string versionOverride = null,
            bool logFailure = true)
        {
            _client = client;
            _logger = logger;
            _logFailure = logFailure;
            _body = body;
            _requestUri = requestUri;
            _method = method;
        }

        public async Task<HttpResponseMessage> ExecuteAsync(int retryCount = 3)
        {
            int retriesRemaining = retryCount;
            // Add a bit of randomness to the retry delay.
            var rng = new Random();

            while (true)
            {
                try
                {
                    using (HttpRequestMessage message = new HttpRequestMessage(_method, _requestUri))
                    {
                        if (!string.IsNullOrEmpty(_body))
                        {
                            message.Content = new StringContent(_body, Encoding.UTF8, "application/json");
                        }

                        HttpResponseMessage response = await _client.SendAsync(message);

                        if (response.StatusCode == HttpStatusCode.NotFound)
                        {
                            if (_logFailure)
                            {
                                _logger.LogError("A 404 (Not Found) was returned. We'll set the retries amount to 0.");
                            }

                            retriesRemaining = 0;
                        }
                        else if (response.StatusCode == HttpStatusCode.BadRequest)
                        {
                            var errorDetails = await response.Content.ReadAsStringAsync();
                            _logger.LogError($"A bad request response was returned from AzDO: {errorDetails}");
                        }

                        response.EnsureSuccessStatusCode();
                        return response;
                    }
                }
                catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException)
                {
                    if (retriesRemaining <= 0)
                    {
                        if (_logFailure)
                        {
                            _logger.LogError($"There was an error executing method '{_method}' against URI '{_requestUri}' " +
                                $"after {retriesRemaining} attempts. Exception: {ex.ToString()}");
                        }
                        throw;
                    }
                    else if (_logFailure)
                    {
                        _logger.LogWarning($"There was an error executing method '{_method}' against URI '{_requestUri}'. " +
                            $"{retriesRemaining} attempts remaining. Exception: {ex.ToString()}");
                    }
                }
                --retriesRemaining;
                int delay = (retryCount - retriesRemaining) * rng.Next(1, 7);
                await Task.Delay(delay * 1000);
            }
        }
    }
}
