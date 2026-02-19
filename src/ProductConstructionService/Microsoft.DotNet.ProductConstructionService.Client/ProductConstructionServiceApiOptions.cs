// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using Azure.Core.Pipeline;
using Azure.Core;
using Maestro.Common.AppCredentials;

namespace Microsoft.DotNet.ProductConstructionService.Client
{
    public partial class ProductConstructionServiceApiOptions
    {
        // https://ms.portal.azure.com/#view/Microsoft_AAD_RegisteredApps/ApplicationMenuBlade/~/Overview/appId/54c17f3d-7325-4eca-9db7-f090bfc765a8/isMSAApp~/false
        private const string MaestroProductionAppId = "54c17f3d-7325-4eca-9db7-f090bfc765a8";

        // https://ms.portal.azure.com/#view/Microsoft_AAD_RegisteredApps/ApplicationMenuBlade/~/Overview/appId/baf98f1b-374e-487d-af42-aa33807f11e4/isMSAApp~/false
        private const string MaestroStagingAppId = "baf98f1b-374e-487d-af42-aa33807f11e4";

        public const string ProductionMaestroUri = "https://maestro.dot.net/";
        // TODO delete this after arcde rollout
        public const string OldProductionMaestroUri = "https://maestro-prod.westus2.cloudapp.azure.com/";

        public const string StagingMaestroUri = "https://maestro.int-dot.net/";
        // TODO delete this after arcde rollout
        public const string OldStagingMaestroUri = "https://maestro-int.westus2.cloudapp.azure.com/";
        public const string PcsLocalUri = "https://localhost:53180/";

        private const string APP_USER_SCOPE = "Maestro.User";

        private static readonly Dictionary<string, string> EntraAppIds = new Dictionary<string, string>
        {
            [StagingMaestroUri.TrimEnd('/')] = MaestroStagingAppId,
            [OldStagingMaestroUri.TrimEnd('/')] = MaestroStagingAppId,
            [PcsLocalUri.TrimEnd('/')] = MaestroStagingAppId,

            [ProductionMaestroUri.TrimEnd('/')] = MaestroProductionAppId,
            [OldProductionMaestroUri.TrimEnd('/')] = MaestroProductionAppId,
        };

        /// <summary>
        /// Gets the Entra app ID for a given Maestro URI
        /// </summary>
        /// <param name="baseUri">The Maestro base URI</param>
        /// <returns>The Entra app ID for the given URI</returns>
        /// <exception cref="ArgumentException">Thrown when the URI is not a known Maestro endpoint</exception>
        public static string GetAppIdForUri(string baseUri)
        {
            string normalizedUri = baseUri.TrimEnd('/');
            
            if (EntraAppIds.TryGetValue(normalizedUri, out string appId))
            {
                return appId;
            }
            
            throw new ArgumentException($"Unknown Maestro URI: {baseUri}. Please use one of the known Maestro endpoints.");
        }

        /// <summary>
        /// Creates a new instance of <see cref="ProductConstructionServiceApiOptions"/> with the provided base URI.
        /// </summary>
        /// <param name="baseUri">API base URI</param>
        /// <param name="accessToken">Optional BAR token. When provided, will be used as the primary auth method.</param>
        /// <param name="managedIdentityId">Managed Identity to use for the auth</param>
        /// <param name="disableInteractiveAuth">Whether to include interactive login flows</param>
        public ProductConstructionServiceApiOptions(string baseUri, string accessToken, string managedIdentityId, bool disableInteractiveAuth)
            : this(
                  new Uri(baseUri),
                  AppCredentialResolver.CreateCredential(
                      new AppCredentialResolverOptions(GetAppIdForUri(baseUri))
                      {
                          DisableInteractiveAuth = disableInteractiveAuth,
                          Token = accessToken,
                          ManagedIdentityId = managedIdentityId,
                          UserScope = APP_USER_SCOPE,
                      }))
        {
        }

        /// <summary>
        /// Creates a new instance of <see cref="ProductConstructionServiceApiOptions"/> with the provided base URI.
        /// </summary>
        /// <param name="accessToken">Optional BAR token. When provided, will be used as the primary auth method.</param>
        /// <param name="managedIdentityId">Managed Identity to use for the auth</param>
        /// <param name="disableInteractiveAuth">Whether to include interactive login flows</param>
        public ProductConstructionServiceApiOptions(string accessToken, string managedIdentityId, bool disableInteractiveAuth)
            : this(ProductionMaestroUri, accessToken, managedIdentityId, disableInteractiveAuth)
        {
        }

        partial void InitializeOptions()
        {
            if (Credentials != null)
            {
                AddPolicy(
                    new BearerTokenAuthenticationPolicy(Credentials, Array.Empty<string>()),
                    HttpPipelinePosition.PerCall);
            }
        }
    }
}
