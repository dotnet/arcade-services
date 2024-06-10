// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Azure.Core;
using Azure.Identity;

namespace ProductConstructionService.Client
{
    /// <summary>
    /// A credential that first tries a user-based browser auth flow then falls back to a managed identity-based flow.
    /// </summary>
    internal class PcsApiCredential : TokenCredential
    {
        private const string TENANT_ID = "72f988bf-86f1-41af-91ab-2d7cd011db47";

        private static readonly Dictionary<string, string> EntraAppIds = new Dictionary<string, string>
        {
            [ProductConstructionServiceApi.StagingPcsBaseUri.TrimEnd('/')] = "baf98f1b-374e-487d-af42-aa33807f11e4",
        };

        private readonly TokenRequestContext _requestContext;
        private readonly TokenCredential _tokenCredential;

        private PcsApiCredential(TokenCredential credential, TokenRequestContext requestContext)
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
        /// Use this for darc invocations from services using an MI
        /// </summary>
        internal static PcsApiCredential CreateManagedIdentityCredential(string barApiBaseUri, string managedIdentityId)
        {
            string appId = EntraAppIds[barApiBaseUri.TrimEnd('/')];

            ManagedIdentityCredential miCredential = new(managedIdentityId);

            ClientAssertionCredential appCredential = new(
                TENANT_ID,
                appId,
                async (ct) => (await miCredential.GetTokenAsync(new TokenRequestContext(new string[] { "api://AzureADTokenExchange" }), ct)).Token);

            TokenRequestContext requestContext = new(new string[] { $"api://{appId}/.default" });
            return new PcsApiCredential(appCredential, requestContext);
        }

        /// <summary>
        /// Use this for darc invocations from pipelines without a token.
        /// </summary>
        internal static PcsApiCredential CreateNonUserCredential(string barApiBaseUri)
        {
            TokenRequestContext requestContext = new(new string[] { $"{EntraAppIds[barApiBaseUri.TrimEnd('/')]}/.default" });
            AzureCliCredential credential = new();
            return new PcsApiCredential(credential, requestContext);
        }
    }
}
