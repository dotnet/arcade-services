// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Azure.Core;
using Newtonsoft.Json.Linq;
using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;

#nullable enable
namespace Microsoft.DotNet.Maestro.Client
{
    internal partial class MaestroApiResponseClassifier
    {
        public override bool IsRetriableException(Exception exception) =>
            base.IsRetriableException(exception)
                || exception is OperationCanceledException
                || exception is HttpRequestException
                || (exception is RestApiException raex && raex.Response.Status >= 500 && raex.Response.Status <= 599)
                || exception is IOException
                || exception is SocketException;
    }

    public partial class MaestroApi
    {
        public const string ProductionBuildAssetRegistryBaseUri = "https://maestro.dot.net/";

        public const string StagingBuildAssetRegistryBaseUri = "https://maestro.int-dot.net/";

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

        /// <summary>
        /// Creates a credential based on parameters provided.
        /// </summary>
        /// <param name="barApiBaseUri">BAR API URI used to determine the right set of credentials (INT vs PROD)</param>
        /// <param name="disableInteractiveAuth">Whether to include interactive login flows</param>
        /// <param name="barApiToken">Token to use for the call. If none supplied, will try other flows.</param>
        /// <param name="federatedToken">Federated token to use for the call. If none supplied, will try other flows.</param>
        /// <returns>Credential that can be used to call the Maestro API</returns>
        public static TokenCredential CreateApiCredential(
            string barApiBaseUri,
            bool disableInteractiveAuth,
            string? barApiToken = null,
            string? federatedToken = null)
        {
            // 1. BAR or Entra token that can directly be used to authenticate against Maestro
            if (!string.IsNullOrEmpty(barApiToken))
            {
                return new MaestroApiTokenCredential(barApiToken!);
            }

            // 2. Federated token that can be used to fetch an app token (CI scenario)
            if (!string.IsNullOrEmpty(federatedToken))
            {
                return MaestroApiCredential.CreateFederatedCredential(barApiBaseUri, federatedToken!);
            }

            barApiBaseUri ??= ProductionBuildAssetRegistryBaseUri;

            // 3. Azure CLI authentication setup by the caller (CI scenario)
            if (disableInteractiveAuth)
            {
                return MaestroApiCredential.CreateNonUserCredential(barApiBaseUri);
            }

            // 4. Interactive login (user-based scenario)
            return MaestroApiCredential.CreateUserCredential(barApiBaseUri);
        }
    }
}
