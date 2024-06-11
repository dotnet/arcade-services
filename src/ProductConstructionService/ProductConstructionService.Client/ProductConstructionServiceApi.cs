// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Azure.Core;

namespace ProductConstructionService.Client
{
    public partial class ProductConstructionServiceApi
    {
        public const string StagingPcsBaseUri = "https://product-construction-int.delightfuldune-c0f01ab0.westus2.azurecontainerapps.io/";

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

            // 2. Managed identity (for server-to-server scenarios)
            if (!string.IsNullOrEmpty(managedIdentityId))
            {
                return PcsApiCredential.CreateManagedIdentityCredential(barApiBaseUri, managedIdentityId!);
            }

            // 3. Azure CLI authentication (for CI scenarios)
            return PcsApiCredential.CreateNonUserCredential(barApiBaseUri);
        }
    }
}
