// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Azure.Core;
using Azure.Identity;
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

        public const string StagingBuildAssetRegistryBaseUri = "https://maestro.dot-int.net/";

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
        /// Creates a credential that should work both in user-based and non-user-based scenarios.
        /// If the old BAR token is supplied, it is used. Otherwise, the secretless auth flows are used.
        /// </summary>
        /// <param name="barApiBaseUri">BAR API URI used to determine the right set of credentials (INT vs PROD)</param>
        /// <param name="barApiPassword">Old BAR PATs created through the Maestro website that will be deprecated soon</param>
        /// <param name="managedIdentityId">Optional managed identity to use (otherwise MI is determined from a hardcoded config)</param>
        /// <returns>Credential that can be used to call the Maestro API</returns>
        public static TokenCredential CreateApiCredential(string barApiBaseUri, string? barApiPassword = null, string? managedIdentityId = null)
        {
            // This will be deprecated once we stop using Maestro tokens
            if (barApiPassword != null)
            {
                return new MaestroApiTokenCredential(barApiPassword);
            }

            barApiBaseUri ??= ProductionBuildAssetRegistryBaseUri;

            return new ChainedTokenCredential(
                MaestroApiCredential.CreateUserCredential(barApiBaseUri),
                MaestroApiCredential.CreateNonUserCredential(barApiBaseUri, managedIdentityId));
        }
    }
}
