// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using Newtonsoft.Json.Linq;
using System.Threading.Tasks;


namespace Microsoft.DotNet.Maestro.Client
{
    partial class MaestroApiResponseClassifier
    {
        public override bool IsRetriableException(Exception exception)
        {
            return base.IsRetriableException(exception) ||
                exception is TaskCanceledException ||
                exception is OperationCanceledException ||
                exception is HttpRequestException ||
                exception is RestApiException raex && raex.Response.Status >= 500 && raex.Response.Status <= 599 ||
                exception is IOException ||
                exception is SocketException;
        }
    }

    partial class MaestroApi
    {
        //Special error handler to consumes the generated MaestroApi code. If this method returns without throwing a specific exception
        //then a generic RestApiException is thrown.
        partial void HandleFailedRequest(RestApiException ex)
        {
            if (ex.Response.Status == (int)HttpStatusCode.BadRequest)
            {
                JObject content;
                try
                {
                    content = JObject.Parse(ex.Response.Content);
                }
                catch (Exception)
                {
                    return;
                }
                if (content["Message"] is JValue value && value.Type == JTokenType.String)
                {
                    string message = (string)value.Value;
                    throw new ArgumentException(message, ex);
                }
            }
        }
    }
}
