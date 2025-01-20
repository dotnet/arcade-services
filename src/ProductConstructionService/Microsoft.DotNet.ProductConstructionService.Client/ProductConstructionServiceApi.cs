// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Azure.Core;
using Newtonsoft.Json.Linq;

#nullable enable
namespace Microsoft.DotNet.ProductConstructionService.Client
{
    public partial interface IProductConstructionServiceApi
    {
        Task<bool> IsAdmin(CancellationToken cancellationToken = default);
    }

    public partial class ProductConstructionServiceApi
    {
        // Special error handler to consumes the generated MaestroApi code. If this method returns without throwing a specific exception
        // then a generic RestApiException is thrown.
        partial void HandleFailedRequest(RestApiException ex)
        {
            if (ex.Response.Status == (int)HttpStatusCode.BadRequest)
            {
                JObject content;
                try
                {
                    content = JObject.Parse(ex.Response.Content);
                    if (content["Message"] is JValue value && value.Type == JTokenType.String)
                    {
                        string? message = content.Value<string>("Message");
                        throw new ArgumentException(message, ex);
                    }
                }
                catch (Exception)
                {
                    return;
                }
            }
            else if (ex.Response.Status == (int)HttpStatusCode.Unauthorized)
            {
                throw new AuthenticationException("Unauthorized access while trying to access Maestro API. " +
                    "Please make sure the PAT you're using is valid.");
            }
        }

        public async Task<bool> IsAdmin(CancellationToken cancellationToken = default)
        {
            var url = new RequestUriBuilder();
            url.Reset(Options.BaseUri);
            url.AppendPath("/Account", false);

            using (var request = Pipeline.CreateRequest())
            {
                request.Uri = url;
                request.Method = RequestMethod.Get;

                using (var response = await SendAsync(request, cancellationToken).ConfigureAwait(false))
                {
                    if (response.Status < 200 || response.Status >= 300 || response.ContentStream == null)
                    {
                        throw new RestApiException(request, response, "Invalid response");
                    }

                    using (var _reader = new StreamReader(response.ContentStream))
                    {
                        var content = await _reader.ReadToEndAsync().ConfigureAwait(false);
                        return content.Trim() == "Admin";
                    }
                }
            }
        }
    }

    internal partial class ProductConstructionServiceApiResponseClassifier
    {
        public override bool IsRetriableException(Exception exception) =>
            base.IsRetriableException(exception)
                || exception is OperationCanceledException
                || exception is HttpRequestException
                || (exception is RestApiException raex && raex.Response.Status >= 500 && raex.Response.Status <= 599)
                || exception is IOException
                || exception is SocketException;
    }
}
