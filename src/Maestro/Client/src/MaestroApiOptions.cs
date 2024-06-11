// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.IO;
using Azure.Core;
using Azure.Core.Pipeline;


namespace Microsoft.DotNet.Maestro.Client
{
    public partial class MaestroApiOptions
    {
        public static readonly string AUTH_CACHE = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".maestro");

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
                  MaestroApi.CreateApiCredential(baseUri, disableInteractiveAuth, accessToken, federatedToken, managedIdentityId))
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
