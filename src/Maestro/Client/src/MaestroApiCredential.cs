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
        private const string USER_SCOPE = "Maestro.User";

        private static readonly Dictionary<string, string> EntraAppIds = new Dictionary<string, string>
        {
            [MaestroApi.StagingBuildAssetRegistryBaseUri.TrimEnd('/')] = "baf98f1b-374e-487d-af42-aa33807f11e4",
            [MaestroApi.OldStagingBuildAssetRegistryBaseUri.TrimEnd('/')] = "baf98f1b-374e-487d-af42-aa33807f11e4",
            [MaestroApi.ProductionBuildAssetRegistryBaseUri.TrimEnd('/')] = "54c17f3d-7325-4eca-9db7-f090bfc765a8",
            [MaestroApi.OldProductionBuildAssetRegistryBaseUri.TrimEnd('/')] = "54c17f3d-7325-4eca-9db7-f090bfc765a8",
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
            string appId = EntraAppIds[barApiBaseUri.TrimEnd('/')];

            // This is a usual credential obtained against an entra app through a browser sign-in
            var credential = new InteractiveBrowserCredential(new InteractiveBrowserCredentialOptions
            {
                TenantId = TENANT_ID,
                ClientId = appId,
                RedirectUri = new Uri("http://localhost"),
                TokenCachePersistenceOptions = new TokenCachePersistenceOptions()
                {
                    Name = "darc"
                }
            });

            var requestContext = new TokenRequestContext(new string[] { $"api://{appId}/{USER_SCOPE}" });
            return new MaestroApiCredential(credential, requestContext);
        }

        /// <summary>
        /// Use this for darc invocations from pipelines with a federated token
        /// </summary>
        internal static MaestroApiCredential CreateFederatedCredential(string barApiBaseUri, string federatedToken)
        {
            string appId = EntraAppIds[barApiBaseUri.TrimEnd('/')];

            var credential = new ClientAssertionCredential(
                TENANT_ID,
                appId,
                token => Task.FromResult(federatedToken));

            var requestContext = new TokenRequestContext(new string[] { $"api://{appId}/.default" });
            return new MaestroApiCredential(credential, requestContext);
        }

        /// <summary>
        /// Use this for darc invocations from services using an MI
        /// </summary>
        internal static MaestroApiCredential CreateManagedIdentityCredential(string barApiBaseUri, string managedIdentityId)
        {
            string appId = EntraAppIds[barApiBaseUri.TrimEnd('/')];

            var miCredential = new ManagedIdentityCredential(managedIdentityId);

            var appCredential = new ClientAssertionCredential(
                TENANT_ID,
                appId,
                async (ct) => (await miCredential.GetTokenAsync(new TokenRequestContext(new string[] { "api://AzureADTokenExchange" }), ct)).Token);

            var requestContext = new TokenRequestContext(new string[] { $"api://{appId}/.default" });
            return new MaestroApiCredential(appCredential, requestContext);
        }

        /// <summary>
        /// Use this for darc invocations from pipelines without a token.
        /// </summary>
        internal static MaestroApiCredential CreateNonUserCredential(string barApiBaseUri)
        {
            var requestContext = new TokenRequestContext(new string[] { $"{EntraAppIds[barApiBaseUri.TrimEnd('/')]}/.default" });
            var credential = new AzureCliCredential();
            return new MaestroApiCredential(credential, requestContext);
        }
    }
}
