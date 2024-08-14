// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using Maestro.Common.AppCredentials;

namespace ProductConstructionService.Client
{
    public partial class ProductConstructionServiceApiOptions
    {
        public const string StagingPcsBaseUri = "https://product-construction-int.delightfuldune-c0f01ab0.westus2.azurecontainerapps.io/";

        private static readonly Dictionary<string, string> EntraAppIds = new Dictionary<string, string>
        {
            [StagingPcsBaseUri.TrimEnd('/')] = "baf98f1b-374e-487d-af42-aa33807f11e4",
        };

        /// <summary>
        /// Creates a new instance of <see cref="ProductConstructionServiceApiOptions"/> with the provided base URI.
        /// </summary>
        /// <param name="baseUri">API base URI</param>
        /// <param name="accessToken">Optional BAR token. When provided, will be used as the primary auth method.</param>
        /// <param name="managedIdentityId">Managed Identity to use for the auth</param>
        public ProductConstructionServiceApiOptions(string baseUri, string accessToken, string managedIdentityId)
            : this(
                  new Uri(baseUri),
                  AppCredentialResolver.CreateCredential(
                      new AppCredentialResolverOptions(EntraAppIds[baseUri.TrimEnd('/')])
                      {
                          DisableInteractiveAuth = true, // the client is only used in Maestro for now
                          Token = accessToken,
                          ManagedIdentityId = managedIdentityId,
                      }))
        {
        }

        /// <summary>
        /// Creates a new instance of <see cref="ProductConstructionServiceApiOptions"/> with the provided base URI.
        /// </summary>
        /// <param name="baseUri">API base URI</param>
        /// <param name="accessToken">Optional BAR token. When provided, will be used as the primary auth method.</param>
        /// <param name="managedIdentityId">Managed Identity to use for the auth</param>
        public ProductConstructionServiceApiOptions(string accessToken, string managedIdentityId)
            : this(StagingPcsBaseUri, accessToken, managedIdentityId)
        {
        }
    }
}
