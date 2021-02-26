using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;

namespace Microsoft.DotNet.Internal.Testing.Utility
{
    public class MockHttpClientFactory : IHttpClientFactory
    {
        private class Handler : HttpMessageHandler
        {
            private readonly IReadOnlyList<CannedResponse> _responses;
            private readonly ICollection<RequestMessage> _unexpectedRequests;

            public Handler(IReadOnlyList<CannedResponse> responses, ICollection<RequestMessage> unexpectedRequests)
            {
                _responses = responses;
                _unexpectedRequests = unexpectedRequests;
            }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                foreach (CannedResponse response in _responses)
                {
                    if (request.RequestUri.AbsoluteUri == response.Uri && request.Method == response.Method)
                    {
                        response.Used = true;
                        return Task.FromResult(CreateResponse(response));
                    }
                }

                _unexpectedRequests.Add(new RequestMessage(request.RequestUri.AbsoluteUri, request.Method, request.Content?.Headers.ContentType.ToString(), request.Content?.Headers.ContentLength ?? 0));
                return Task.FromResult(CreateResponse(HttpStatusCode.NoContent));
            }

            private static HttpResponseMessage CreateResponse(HttpStatusCode code)
            {
                return new HttpResponseMessage(code);
            }

            private static HttpResponseMessage CreateResponse(CannedResponse response)
            {
                var message = new HttpResponseMessage(response.Code)
                {
                    Content = string.IsNullOrEmpty(response.Content) ? null : new StringContent(response.Content)
                    {
                        Headers =
                        {
                            ContentType = new MediaTypeHeaderValue(response.ContentType),
                        },
                    },
                };
                foreach (KeyValuePair<string, string> header in response.Headers)
                {
                    message.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }

                return message;
            }
        }

        private class RequestMessage
        {
            public readonly string Uri;
            public readonly HttpMethod Method;
            public readonly string ContentType;
            public readonly long ContentLength;

            public RequestMessage(string uri, HttpMethod method, string contentType, long contentLength)
            {
                Uri = uri;
                Method = method;
                ContentType = contentType;
                ContentLength = contentLength;
            }

            public override string ToString()
            {
                return $"{Method} {Uri}{(ContentLength > 0 ? $" {ContentLength} byte {ContentType}" : "")}";
            }
        }

        private class CannedResponse
        {
            public readonly string Uri;
            public readonly string Content;
            public readonly HttpMethod Method;
            public readonly string ContentType;
            public readonly HttpStatusCode Code;
            public readonly IReadOnlyDictionary<string, string> Headers;
            public bool Used;

            public CannedResponse(string uri, string content, HttpMethod method, string contentType,
                HttpStatusCode code, IReadOnlyDictionary<string, string> headers)
            {
                Uri = uri;
                Content = content;
                Method = method;
                ContentType = contentType;
                Code = code;
                Headers = headers;
                Used = false;
            }

            public override string ToString()
            {
                return $"{Method} {Uri} {Code}{(!string.IsNullOrEmpty(Content) ? $" {Content.Length} byte {ContentType}" : "")}";
            }
        }

        private static readonly IReadOnlyDictionary<string, string> EmptyHeaders = new Dictionary<string, string>();

        private readonly List<CannedResponse> _cannedResponses = new List<CannedResponse>();
        private readonly List<RequestMessage> _unexpectedRequests = new List<RequestMessage>();

        public void AddCannedResponse(string uri, string content, HttpStatusCode code, string contentType, HttpMethod method, IReadOnlyDictionary<string, string> headers)
        {
            _cannedResponses.Add(new CannedResponse(uri, content, method, contentType, code, headers));
        }

        public void AddCannedResponse(string uri, string content, HttpStatusCode code, string contentType, HttpMethod method)
        {
            AddCannedResponse(uri, content, code, contentType, method, EmptyHeaders);
        }
        
        public void AddCannedResponse(string uri, string content, HttpStatusCode code, string contentType)
        {
            AddCannedResponse(uri, content, code, contentType, HttpMethod.Get);
        }

        public void AddCannedResponse(string uri, string content, HttpStatusCode code)
        {
            AddCannedResponse(uri, content, code, "application/json");
        }

        public void AddCannedResponse(string uri, string content)
        {
            AddCannedResponse(uri, content, HttpStatusCode.OK);
        }

        public HttpClient CreateClient(string name)
        {
            return new HttpClient(new Handler(_cannedResponses, _unexpectedRequests));
        }

        public void VerifyAll()
        {
            _cannedResponses.Should().AllSatisfy(response => response.Used.Should().BeTrue());
            _unexpectedRequests.Should().BeEmpty();
        }
    }
}
