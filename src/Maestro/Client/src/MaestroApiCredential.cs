// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.IO;
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

        private const string AUTH_RECORD_PREFIX = ".auth-record";

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
        internal static MaestroApiCredential CreateUserCredential(string barApiBaseUri, string cachePath = "")
        {
            string appId = EntraAppIds[barApiBaseUri.TrimEnd('/')];
            var requestContext = new TokenRequestContext(new string[] { $"api://{appId}/{USER_SCOPE}" });

            // This is a usual configuration for a credential obtained against an entra app through a browser sign-in
            var credentialOptions = new InteractiveBrowserCredentialOptions
            {
                TenantId = TENANT_ID,
                ClientId = appId,
                RedirectUri = new Uri("http://localhost"),
                TokenCachePersistenceOptions = new TokenCachePersistenceOptions()
                {
                    Name = "darc"
                },
            };

            InteractiveBrowserCredential credential;
            if (!string.IsNullOrEmpty(cachePath))
            {
                string authRecordPath = Path.Combine(cachePath, $"{AUTH_RECORD_PREFIX}-{appId}");
                credential = GetCredentialFromCache(credentialOptions, requestContext, authRecordPath);
            }
            else
            {
                // In certain cases like MSBuild tasks we don't have a cache to work with and
                // always prompt the user for concent
                credential = new InteractiveBrowserCredential(credentialOptions);
            }

            return new MaestroApiCredential(credential, requestContext);
        }

        /// <summary>
        /// Create interactive credential from an authentication record stored in local cache
        /// Authentication record is a set of app and user-specific metadata used by the library to authenticate
        /// </summary>
        private static InteractiveBrowserCredential GetCredentialFromCache(
            InteractiveBrowserCredentialOptions credentialOptions,
            TokenRequestContext requestContext,
            string authRecordPath)
        {
            InteractiveBrowserCredential credential;

            if (File.Exists(authRecordPath))
            {
                // Fetch existing authentication record to not promt the user for concent
                using var authRecordStream = new FileStream(authRecordPath, FileMode.Open, FileAccess.Read);
                var authRecord = AuthenticationRecord.Deserialize(authRecordStream);

                credentialOptions.AuthenticationRecord = authRecord;

                credential = new InteractiveBrowserCredential(credentialOptions);
            }
            else
            {
                credential = new InteractiveBrowserCredential(credentialOptions);

                // Prompt the user for concent and save the resulting authentication record on disk
                var authRecord = credential.Authenticate(requestContext);

                using var authRecordStream = new FileStream(authRecordPath, FileMode.Create, FileAccess.Write);
                authRecord.Serialize(authRecordStream);
            }

            return credential;
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
