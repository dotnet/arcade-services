// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using Azure.Core;
using Azure.Core.Pipeline;
using Maestro.Common.AppCredentials;

namespace Microsoft.DotNet.Maestro.Client
{
    public partial class MaestroApiOptions
    {
        public const string ProductionBuildAssetRegistryBaseUri = "https://maestro.dot.net/";
        public const string OldProductionBuildAssetRegistryBaseUri = "https://maestro-prod.westus2.cloudapp.azure.com/";

        // https://ms.portal.azure.com/#view/Microsoft_AAD_RegisteredApps/ApplicationMenuBlade/~/Overview/appId/54c17f3d-7325-4eca-9db7-f090bfc765a8/isMSAApp~/false
        private const string MaestroProductionAppId = "54c17f3d-7325-4eca-9db7-f090bfc765a8";

        public const string StagingBuildAssetRegistryBaseUri = "https://maestro.int-dot.net/";
        public const string OldStagingBuildAssetRegistryBaseUri = "https://maestro-int.westus2.cloudapp.azure.com/";

        // https://ms.portal.azure.com/#view/Microsoft_AAD_RegisteredApps/ApplicationMenuBlade/~/Overview/appId/baf98f1b-374e-487d-af42-aa33807f11e4/isMSAApp~/false
        private const string MaestroStagingAppId = "baf98f1b-374e-487d-af42-aa33807f11e4";

        private const string APP_USER_SCOPE = "Maestro.User";

        private static readonly Dictionary<string, string> EntraAppIds = new Dictionary<string, string>
        {
            [StagingBuildAssetRegistryBaseUri.TrimEnd('/')] = MaestroStagingAppId,
            [OldStagingBuildAssetRegistryBaseUri.TrimEnd('/')] = MaestroStagingAppId,
            [ProductionBuildAssetRegistryBaseUri.TrimEnd('/')] = MaestroProductionAppId,
            [OldProductionBuildAssetRegistryBaseUri.TrimEnd('/')] = MaestroProductionAppId,
        };

        /// <summary>
        /// Creates a new instance of <see cref="MaestroApiOptions"/> with the provided base URI.
        /// </summary>
        /// <param name="baseUri">API base URI</param>
        /// <param name="accessToken">Optional BAR token. When provided, will be used as the primary auth method.</param>
        /// <param name="managedIdentityId">Managed Identity to use for the auth</param>
        /// <param name="federatedToken">Optional federated token. When provided, will be used as the primary auth method.</param>
        /// <param name="disableInteractiveAuth">Whether to include interactive login flows</param>
        public MaestroApiOptions(string baseUri, string accessToken, string managedIdentityId, string federatedToken, bool disableInteractiveAuth)
            : this(
                  new Uri(baseUri),
                  AppCredentialResolver.CreateCredential(
                      new CredentialResolverOptions(EntraAppIds[(baseUri ?? ProductionBuildAssetRegistryBaseUri).TrimEnd('/')])
                      {
                          DisableInteractiveAuth = disableInteractiveAuth,
                          Token = accessToken,
                          FederatedToken = federatedToken,
                          ManagedIdentityId = managedIdentityId,
                          UserScope = APP_USER_SCOPE,
                      }))
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
