// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.DotNet.Internal.Health.Tests
{
    public class MockHandler : HttpMessageHandler, IHttpClientFactory, IHttpMessageHandlerFactory
    {
        private readonly Func<HttpRequestMessage, Task<HttpResponseMessage>> _callback;

        public MockHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> callback)
        {
            _callback = callback;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return _callback(request);
        }

        public HttpClient CreateClient(string name)
        {
            return new HttpClient(CreateHandler(name));
        }

        public HttpMessageHandler CreateHandler(string name)
        {
            return this;
        }
    }
}
