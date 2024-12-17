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
        // https://ms.portal.azure.com/#view/Microsoft_AAD_RegisteredApps/ApplicationMenuBlade/~/Overview/appId/54c17f3d-7325-4eca-9db7-f090bfc765a8/isMSAApp~/false
        private const string MaestroProductionAppId = "54c17f3d-7325-4eca-9db7-f090bfc765a8";

        // https://ms.portal.azure.com/#view/Microsoft_AAD_RegisteredApps/ApplicationMenuBlade/~/Overview/appId/baf98f1b-374e-487d-af42-aa33807f11e4/isMSAApp~/false
        private const string MaestroStagingAppId = "baf98f1b-374e-487d-af42-aa33807f11e4";

        public const string ProductionMaestroUri = "https://maestro.dot.net/";
        public const string OldProductionMaestroUri = "https://maestro-prod.westus2.cloudapp.azure.com/";

        public const string StagingMaestroUri = "https://maestro.int-dot.net/";
        public const string OldPcsStagingUri = "https://maestro-int.westus2.cloudapp.azure.com/";
        public const string PcsProdUri = "https://product-construction-prod.wittysky-0c79e3cc.westus2.azurecontainerapps.io/";
        public const string PcsStagingUri = "https://product-construction-int.agreeablesky-499be9de.westus2.azurecontainerapps.io/";
        public const string PcsLocalUri = "https://localhost:53180/";
        public const string PcsTestUri = "https://maestro-int-ag.westus2.cloudapp.azure.com/";


        private const string APP_USER_SCOPE = "Maestro.User";

        private static readonly Dictionary<string, string> EntraAppIds = new Dictionary<string, string>
        {
            [StagingMaestroUri.TrimEnd('/')] = MaestroStagingAppId,
            [OldPcsStagingUri.TrimEnd('/')] = MaestroStagingAppId,
            [PcsStagingUri.TrimEnd('/')] = MaestroStagingAppId,
            [PcsLocalUri.TrimEnd('/')] = MaestroStagingAppId,
            [PcsTestUri.TrimEnd('/')] = MaestroStagingAppId,

            [PcsProdUri.TrimEnd('/')] = MaestroProductionAppId,
            [ProductionMaestroUri.TrimEnd('/')] = MaestroProductionAppId,
            [OldProductionMaestroUri.TrimEnd('/')] = MaestroProductionAppId,
        };

        /// <summary>
        /// Creates a new instance of <see cref="MaestroApiOptions"/> with the provided base URI.
        /// </summary>
        /// <param name="baseUri">API base URI</param>
        /// <param name="accessToken">Optional BAR token. When provided, will be used as the primary auth method.</param>
        /// <param name="managedIdentityId">Managed Identity to use for the auth</param>
        /// <param name="disableInteractiveAuth">Whether to include interactive login flows</param>
        public MaestroApiOptions(string baseUri, string accessToken, string managedIdentityId, bool disableInteractiveAuth)
            : this(
                  new Uri(baseUri),
                  AppCredentialResolver.CreateCredential(
                      new AppCredentialResolverOptions(EntraAppIds[(baseUri ?? ProductionMaestroUri).TrimEnd('/')])
                      {
                          DisableInteractiveAuth = disableInteractiveAuth,
                          Token = accessToken,
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
