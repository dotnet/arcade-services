// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Reflection;
using Azure.Core.Pipeline;
using Azure.Core;
using Maestro.Common.AppCredentials;
using Microsoft.Extensions.Logging;

namespace Microsoft.DotNet.ProductConstructionService.Client
{
    public partial class ProductConstructionServiceApiOptions
    {
        /// <summary>
        /// Sentinel used for <see cref="ClientName"/> when no override is supplied.
        /// Sent verbatim on the wire.
        /// </summary>
        public const string UnknownClientIdentity = "default";

        /// <summary>
        /// Effective client name sent in the <c>X-Client-Name</c> header. Set from the
        /// constructor override, or <see cref="UnknownClientIdentity"/> when none is supplied.
        /// </summary>
        public string ClientName { get; private set; }

        /// <summary>
        /// Effective client version sent in the <c>X-Client-Version</c> header. Set from the
        /// constructor override, falling back to the entry assembly's informational version.
        /// </summary>
        public string ClientVersion { get; private set; }

        private static string ResolveClientVersion(string clientVersionOverride)
        {
            if (clientVersionOverride != null)
            {
                return clientVersionOverride;
            }

            string informationalVersion = Assembly.GetEntryAssembly()
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                .InformationalVersion;

            // Strip the SourceLink "+<commit-sha>" suffix.
            int plusIndex = informationalVersion.IndexOf('+');
            return plusIndex >= 0 ? informationalVersion.Substring(0, plusIndex) : informationalVersion;
        }

        /// <summary>
        /// Creates a new instance of <see cref="ProductConstructionServiceApiOptions"/> with the provided base URI,
        /// credentials, and client identity overrides used for the <c>X-Client-Name</c> /
        /// <c>X-Client-Version</c> headers.
        /// </summary>
        public ProductConstructionServiceApiOptions(Uri baseUri, TokenCredential credentials, string clientName, string clientVersion)
        {
            ClientName = string.IsNullOrWhiteSpace(clientName) ? UnknownClientIdentity : clientName;
            ClientVersion = ResolveClientVersion(clientVersion);
            BaseUri = baseUri;
            Credentials = credentials;
            InitializeOptions();
        }
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
        /// <param name="loggerFactory">Optional logger factory used by the interactive credential to emit progress messages.</param>
        /// <param name="clientName">Optional override for the <c>X-Client-Name</c> header.</param>
        /// <param name="clientVersion">Optional override for the <c>X-Client-Version</c> header.</param>
        public ProductConstructionServiceApiOptions(string baseUri, string accessToken, string managedIdentityId, bool disableInteractiveAuth, ILoggerFactory loggerFactory = null, string clientName = null, string clientVersion = null)
            : this(
                  new Uri(baseUri),
                  AppCredentialResolver.CreateCredential(
                      new AppCredentialResolverOptions(GetAppIdForUri(baseUri))
                      {
                          DisableInteractiveAuth = disableInteractiveAuth,
                          Token = accessToken,
                          ManagedIdentityId = managedIdentityId,
                          UserScope = APP_USER_SCOPE,
                      },
                      loggerFactory),
                  clientName,
                  clientVersion)
        {
        }

        /// <summary>
        /// Creates a new instance of <see cref="ProductConstructionServiceApiOptions"/> with the provided base URI.
        /// </summary>
        /// <param name="accessToken">Optional BAR token. When provided, will be used as the primary auth method.</param>
        /// <param name="managedIdentityId">Managed Identity to use for the auth</param>
        /// <param name="disableInteractiveAuth">Whether to include interactive login flows</param>
        /// <param name="loggerFactory">Optional logger factory used by the interactive credential to emit progress messages.</param>
        /// <param name="clientName">Optional override for the <c>X-Client-Name</c> header.</param>
        /// <param name="clientVersion">Optional override for the <c>X-Client-Version</c> header.</param>
        public ProductConstructionServiceApiOptions(string accessToken, string managedIdentityId, bool disableInteractiveAuth, ILoggerFactory loggerFactory = null, string clientName = null, string clientVersion = null)
            : this(ProductionMaestroUri, accessToken, managedIdentityId, disableInteractiveAuth, loggerFactory, clientName, clientVersion)
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

            // Always-on client identity headers so the server can identify the caller and
            // (for darc) enforce a minimum version.
            AddPolicy(
                new ClientIdentityHeaderPolicy(ClientName, ClientVersion),
                HttpPipelinePosition.PerCall);
        }
    }
}
