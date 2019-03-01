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

        public async Task<HttpResponseMessage> ExecuteAsync()
        {
            HttpRequestMessage message = new HttpRequestMessage(_method, _requestUri);
            if (!string.IsNullOrEmpty(_body))
            {
                message.Content = new StringContent(_body, Encoding.UTF8, "application/json");
            }
            HttpResponseMessage response = await _client.SendAsync(message);

            if (!response.IsSuccessStatusCode)
            {
                if (_logFailure)
                {
                    _logger.LogError(
                        $"There was an error executing method '{message.Method}' against URI '{message.RequestUri}'. " +
                        $"Request failed with error code: '{response.StatusCode}'");
                }
                response.EnsureSuccessStatusCode();
            }

            return response;
        }
    }
}
