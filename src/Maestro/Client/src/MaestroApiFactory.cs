// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;

#nullable enable
namespace Microsoft.DotNet.Maestro.Client
{
    public static class MaestroApiFactory
    {
        /// <summary>
        /// Obtains API client for authenticated access to Maestro.
        /// </summary>
        /// <param name="baseUri">URI of the build asset registry service to use.</param>
        /// <param name="accessToken">Optional BAR token. When provided, will be used as the primary auth method.</param>
        /// <param name="managedIdentityId">Managed Identity to use for the auth</param>
        /// <param name="disableInteractiveAuth">Whether to include interactive login flows</param>
        public static IMaestroApi GetAuthenticated(
            string baseUri,
            string? accessToken,
            string? managedIdentityId,
            bool disableInteractiveAuth)
        {
            return new MaestroApi(new MaestroApiOptions(
                baseUri,
                accessToken,
                managedIdentityId,
                disableInteractiveAuth));
        }

        /// <summary>
        /// Obtains API client for authenticated access to Maestro.
        /// </summary>
        /// <param name="accessToken">Optional BAR token. When provided, will be used as the primary auth method.</param>
        /// <param name="managedIdentityId">Managed Identity to use for the auth</param>
        /// <param name="disableInteractiveAuth">Whether to include interactive login flows</param>
        public static IMaestroApi GetAuthenticated(
            string? accessToken,
            string? managedIdentityId,
            bool disableInteractiveAuth)
        {
            return new MaestroApi(new MaestroApiOptions(
                MaestroApiOptions.StagingMaestroUri,
                accessToken,
                managedIdentityId,
                disableInteractiveAuth));
        }

        /// <summary>
        /// Obtains API client for non-authenticated access to Maestro.
        /// </summary>
        public static IMaestroApi GetAnonymous()
        {
            return new MaestroApi(new MaestroApiOptions());
        }

        /// <summary>
        /// Obtains API client for non-authenticated access to Maestro.
        /// </summary>
        public static IMaestroApi GetAnonymous(string baseUri)
        {
            return new MaestroApi(new MaestroApiOptions(new Uri(baseUri)));
        }
    }
}
