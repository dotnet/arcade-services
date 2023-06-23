using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.DotNet.AzureDevOpsTimeline.Tests;

public class MockExceptionThrowingHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _sendAsync;
    private int _throwAfter;
    private int _counter;

    public MockExceptionThrowingHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> sendAsync, int throwAfter)
    {
        _sendAsync = sendAsync;
        _throwAfter = throwAfter;
        _counter = 0;
    }

    public static MockExceptionThrowingHandler Create(string message, int throwAfter)
    {
        return new MockExceptionThrowingHandler(async (request, cancellationToken) =>
        {
            var responseMessage = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(message)
            };

            return await Task.FromResult(responseMessage);
        }, throwAfter);
    }

    public static MockExceptionThrowingHandler Create(int throwAfter)
    {
        return new MockExceptionThrowingHandler(async (request, cancellationToken) =>
        {
            var responseMessage = new HttpResponseMessage(HttpStatusCode.OK);

            return await Task.FromResult(responseMessage);
        }, throwAfter);
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (Interlocked.Increment(ref _counter) <= _throwAfter)
        {
            return await _sendAsync(request, cancellationToken);
        }
        throw new OperationCanceledException();
    }
}
