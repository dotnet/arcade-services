using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.DotNet.AzureDevOpsTimeline.Tests
{
    public class MockExceptionThrowingDelegatingHandler : DelegatingHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _sendAsync;
        private int _throwAfter;
        private int _counter;

        public MockExceptionThrowingDelegatingHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> sendAsync, int throwAfter)
        {
            _sendAsync = sendAsync;
            _throwAfter = throwAfter;
            _counter = 0;
        }

        public static MockExceptionThrowingDelegatingHandler Create(string message, int throwAfter)
        {
            return new MockExceptionThrowingDelegatingHandler(async (request, cancellationToken) =>
            {
                var responseMessage = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(message)
                };

                return await Task.FromResult(responseMessage);
            }, throwAfter);
        }

        public static MockExceptionThrowingDelegatingHandler Create(int throwAfter)
        {
            return new MockExceptionThrowingDelegatingHandler(async (request, cancellationToken) =>
            {
                var responseMessage = new HttpResponseMessage(HttpStatusCode.OK);

                return await Task.FromResult(responseMessage);
            }, throwAfter);
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _counter) <= _throwAfter)
            {
                // I think we need to call the HttpClientHandler to get the CheckCertificateRevocation stuff done
                // This call is going to fail so we're just ingoring it and doing what we want
                try
                {
                    await base.SendAsync(request, cancellationToken);
                }
                catch (Exception) { }

                return await _sendAsync(request, cancellationToken);
            }
            throw new OperationCanceledException();
        }
    }
}
