// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using Newtonsoft.Json.Linq;
using System.Net;
using System.IO;
using System.Net.Http;
using System.Net.Sockets;

#nullable enable
namespace Microsoft.DotNet.ProductConstructionService.Client
{
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
