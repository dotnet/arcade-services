using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.DotNet.AzureDevOpsTimeline.Tests
{
    public class MockHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _sendAsync;

        public int RequestCancelledCount { get; private set; }

        public MockHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> sendAsync)
        {
            _sendAsync = sendAsync;
        }

        public static MockHttpMessageHandler Create(string message)
        {
            return new MockHttpMessageHandler(async (request, cancellationToken) =>
            {
                var responseMessage = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(message)
                };

                return await Task.FromResult(responseMessage);
            });
        }

        public static MockHttpMessageHandler CreateWithCustomHttpStatusCode(HttpStatusCode httpStatusCode)
        {
            return new MockHttpMessageHandler(async (request, cancellationToken) =>
            {
                var responseMessage = new HttpResponseMessage(httpStatusCode);

                return await Task.FromResult(responseMessage);
            });
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            try
            {
                await Task.Delay(1000, cancellationToken);

                return await _sendAsync(request, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                RequestCancelledCount++;
                throw;
            }
        }
    }
}
