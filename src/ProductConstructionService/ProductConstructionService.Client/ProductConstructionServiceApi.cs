// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using Azure.Core;
using Microsoft.DotNet.Maestro.Common;

namespace ProductConstructionService.Client
{
    public partial class ProductConstructionServiceApi
    {
        public const string StagingPcsBaseUri = "https://product-construction-int.delightfuldune-c0f01ab0.westus2.azurecontainerapps.io/";

        private static readonly Dictionary<string, string> EntraAppIds = new Dictionary<string, string>
        {
            [StagingPcsBaseUri.TrimEnd('/')] = "baf98f1b-374e-487d-af42-aa33807f11e4",
        };

        /// <summary>
        /// Creates a credential based on parameters provided.
        /// </summary>
        /// <param name="barApiBaseUri">BAR API URI used to determine the right set of credentials (INT vs PROD)</param>
        /// <param name="barApiToken">Token to use for the call. If none supplied, will try other flows.</param>
        /// <param name="managedIdentityId">Managed Identity to use for the auth</param>
        /// <returns>Credential that can be used to call the Maestro API</returns>
        public static TokenCredential CreateApiCredential(
            string barApiBaseUri,
            string barApiToken = null,
            string managedIdentityId = null)
        {
            // 1. BAR or Entra token that can directly be used to authenticate against Maestro
            if (!string.IsNullOrEmpty(barApiToken))
            {
                return new PcsApiTokenCredential(barApiToken!);
            }

            barApiBaseUri ??= StagingPcsBaseUri;
            string appId = EntraAppIds[barApiBaseUri.TrimEnd('/')];

            // 2. Managed identity (for server-to-server scenarios)
            if (!string.IsNullOrEmpty(managedIdentityId))
            {
                return AppCredential.CreateManagedIdentityCredential(appId, managedIdentityId!);
            }

            // 3. Azure CLI authentication (for CI scenarios)
            return AppCredential.CreateNonUserCredential(appId);
        }
    }
}
