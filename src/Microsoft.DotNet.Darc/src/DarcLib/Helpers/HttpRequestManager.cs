// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Microsoft.DotNet.DarcLib
{
    public class HttpRequestManager
    {
        private readonly HttpClient _client;
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

        public async Task<HttpResponseMessage> ExecuteAsync(int retryCount = 15)
        {
            int retriesRemaining = retryCount;
            // Add a bit of randomness to the retry delay.
            var rng = new Random();

            while (true)
            {
                try
                {
                    HttpResponseMessage response = await _client.SendAsync(_message);
                    response.EnsureSuccessStatusCode();
                    return response;
                }
                catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException)
                {
                    if (retriesRemaining <= 0)
                    {
                        if (_logFailure)
                        {
                            _logger.LogError($"There was an error executing method '{_message.Method}' against URI '{_message.RequestUri}' " +
                                $"after {retriesRemaining} attempts. Exception: {ex.ToString()}");
                        }
                        throw;
                    }
                    else if (_logFailure)
                    {
                        _logger.LogWarning($"There was an error executing method '{_message.Method}' against URI '{_message.RequestUri}'. " +
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
