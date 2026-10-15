// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Octokit;
using Octokit.Caching;
using Octokit.Internal;

#nullable enable
namespace Microsoft.DotNet.DarcLib;

internal sealed class GitHubConditionalRequestClient(IHttpClient client, IResponseCache cache) : IHttpClient
{
    public async Task<IResponse> Send(
        IRequest request, CancellationToken cancellationToken, Func<object, object>? preprocessResponseBody = null)
    {
        if (request.Method != HttpMethod.Get)
        {
            return await client.Send(request, cancellationToken, preprocessResponseBody);
        }

        var cached = await cache.GetAsync(request);
        if (cached != null && !string.IsNullOrEmpty(cached.ApiInfo.Etag))
        {
            request.Headers["If-None-Match"] = cached.ApiInfo.Etag;
        }

        // Cache the unprocessed body so each caller can apply its own response preprocessing.
        var response = await client.Send(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotModified && cached != null)
        {
            response = cached;
        }
        else if (response.StatusCode == HttpStatusCode.OK)
        {
            await cache.SetAsync(request, CachedResponse.V1.Create(response));
        }

        if (preprocessResponseBody != null)
        {
            response = new CachedResponse.V1(
                preprocessResponseBody(response.Body), response.Headers, response.ApiInfo,
                response.StatusCode, response.ContentType);
        }

        return response;
    }

    public void SetRequestTimeout(TimeSpan timeout) => client.SetRequestTimeout(timeout);

    public void Dispose() => client.Dispose();
}
