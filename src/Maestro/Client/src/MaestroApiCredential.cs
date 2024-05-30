// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Azure.Core;
using Azure.Identity;

#nullable enable
namespace Microsoft.DotNet.Maestro.Client
{
    /// <summary>
    /// A credential that first tries a user-based browser auth flow then falls back to a managed identity-based flow.
    /// </summary>
    internal class MaestroApiCredential : TokenCredential
    {
        private const string TENANT_ID = "72f988bf-86f1-41af-91ab-2d7cd011db47";

        private static readonly Dictionary<string, (string ManagedIdentityId, string EntraAppId)> WellKnownAuthIds = new Dictionary<string, (string ManagedIdentityId, string EntraAppId)>
        {
            [MaestroApi.StagingBuildAssetRegistryBaseUri] = ("610d68a4-8c62-46b0-b0b9-e859e7f3564e", "baf98f1b-374e-487d-af42-aa33807f11e4"),
            [MaestroApi.ProductionBuildAssetRegistryBaseUri] = ("TODO", "TODO"),
        };

        private readonly TokenRequestContext _requestContext;
        private readonly TokenCredential _tokenCredential;

        private MaestroApiCredential(TokenCredential credential, TokenRequestContext requestContext)
        {
            _requestContext = requestContext;
            _tokenCredential = credential;
        }

        public override AccessToken GetToken(TokenRequestContext _, CancellationToken cancellationToken)
        {
            // We hardcode the request context as we know which scopes we need to invoke in each scenario (user vs daemon)
            return _tokenCredential.GetToken(_requestContext, cancellationToken);
        }

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext _, CancellationToken cancellationToken)
        {
            // We hardcode the request context as we know which scopes we need to invoke in each scenario (user vs daemon)
            return _tokenCredential.GetTokenAsync(_requestContext, cancellationToken);
        }

        /// <summary>
        /// Use this for user-based flows (darc invocation from dev machines).
        /// </summary>
        internal static MaestroApiCredential CreateUserCredential(string barApiBaseUri)
        {
            // This is a usual credential obtained against an entra app through a browser sign-in
            var credential = new InteractiveBrowserCredential(new InteractiveBrowserCredentialOptions
            {
                TenantId = TENANT_ID,
                ClientId = WellKnownAuthIds[barApiBaseUri].EntraAppId,
                RedirectUri = new Uri("http://localhost"),
                TokenCachePersistenceOptions = new TokenCachePersistenceOptions()
                {
                    Name = "darc"
                }
            });

            var requestContext = new TokenRequestContext(new string[] { $"api://{WellKnownAuthIds[barApiBaseUri].EntraAppId}/Maestro.User" });
            return new MaestroApiCredential(credential, requestContext);
        }

        /// <summary>
        /// Use this for non-user-based flows (darc invocation from pipelines).
        /// </summary>
        internal static MaestroApiCredential CreateNonUserCredential(string barApiBaseUri, string? managedIdentityId = null)
        {
            string appId = WellKnownAuthIds[barApiBaseUri].EntraAppId;

            // This is a required audience during federated credential token creation
            var miRequestContext = new TokenRequestContext(new string[] { "api://AzureADTokenExchange/.default" });
            var appRequestContext = new TokenRequestContext(new string[] { $"{appId}/.default" });

            // This credential is obtained through a managed identity
            // which is added as a federated credential onto the entra app
            var miCreds = new ManagedIdentityCredential(managedIdentityId ?? WellKnownAuthIds[barApiBaseUri].ManagedIdentityId);
            var credential = new ClientAssertionCredential(
               TENANT_ID,
               appId,
               async (ct) => (await miCreds.GetTokenAsync(miRequestContext, ct).ConfigureAwait(false)).Token);

            return new MaestroApiCredential(credential, appRequestContext);
        }
    }
}
