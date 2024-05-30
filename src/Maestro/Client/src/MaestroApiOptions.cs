// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using Azure.Core;
using Azure.Core.Pipeline;


namespace Microsoft.DotNet.Maestro.Client
{
    public partial class MaestroApiOptions
    {
        /// <summary>
        /// Creates a new instance of <see cref="MaestroApiOptions"/> with the provided base URI.
        /// </summary>
        /// <param name="baseUri">API base URI</param>
        /// <param name="accessToken">Optional BAR token. When provided, will be used as the primary auth method.</param>
        public MaestroApiOptions(string baseUri, string accessToken)
            : this(new Uri(baseUri), string.IsNullOrEmpty(accessToken) ? MaestroApi.CreateApiCredential(baseUri, accessToken) : null)
        {
        }

        partial void InitializeOptions()
        {
            if (Credentials != null)
            {
                AddPolicy(
                    new BearerTokenAuthenticationPolicy(Credentials, Array.Empty<string>()),
                    HttpPipelinePosition.PerCall
                    );
            }
        }
    }
}
