// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;

namespace ProductConstructionService.Client
{
    public partial class ProductConstructionServiceApiOptions
    {
        /// <summary>
        /// Creates a new instance of <see cref="ProductConstructionServiceApiOptions"/> with the provided base URI.
        /// </summary>
        /// <param name="baseUri">API base URI</param>
        /// <param name="accessToken">Optional BAR token. When provided, will be used as the primary auth method.</param>
        /// <param name="managedIdentityId">Managed Identity to use for the auth</param>
        public ProductConstructionServiceApiOptions(string baseUri, string accessToken, string managedIdentityId)
            : this(
                  new Uri(baseUri),
                  ProductConstructionServiceApi.CreateApiCredential(baseUri, accessToken, managedIdentityId))
        {
        }

        /// <summary>
        /// Creates a new instance of <see cref="ProductConstructionServiceApiOptions"/> with the provided base URI.
        /// </summary>
        /// <param name="baseUri">API base URI</param>
        /// <param name="accessToken">Optional BAR token. When provided, will be used as the primary auth method.</param>
        /// <param name="managedIdentityId">Managed Identity to use for the auth</param>
        public ProductConstructionServiceApiOptions(string accessToken, string managedIdentityId)
            : this(
                  new Uri(ProductConstructionServiceApi.StagingPcsBaseUri),
                  ProductConstructionServiceApi.CreateApiCredential(ProductConstructionServiceApi.StagingPcsBaseUri, accessToken, managedIdentityId))
        {
        }
    }
}
