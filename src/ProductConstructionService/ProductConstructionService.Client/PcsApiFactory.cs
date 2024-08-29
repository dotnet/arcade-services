// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;

#nullable enable
namespace ProductConstructionService.Client
{
    public static class PcsApiFactory
    {
        /// <summary>
        /// Obtains API client for authenticated access to internal queues.
        /// The client will access production ProductConstructionService instance.
        /// </summary>
        /// <param name="accessToken">Optional BAR token. When provided, will be used as the primary auth method.</param>
        /// <param name="managedIdentityId">Managed Identity to use for the auth</param>
        /// <param name="disableInteractiveAuth">Whether to include interactive login flows</param>
        public static IProductConstructionServiceApi GetAuthenticated(
            string? accessToken,
            string? managedIdentityId,
            bool disableInteractiveAuth)
        {
            return new ProductConstructionServiceApi(new ProductConstructionServiceApiOptions(
                accessToken,
                managedIdentityId,
                disableInteractiveAuth));
        }

        /// <summary>
        /// Obtains API client for unauthenticated access to external queues.
        /// The client will access production ProductConstructionService instance.
        /// </summary>
        /// <remarks>
        /// Attempt to access internal queues by such client will cause
        /// <see cref="ArgumentException"/> triggered by <c>SendAsync</c> call.
        /// </remarks>
        public static IProductConstructionServiceApi GetAnonymous()
        {
            return new ProductConstructionServiceApi(new ProductConstructionServiceApiOptions());
        }

        /// <summary>
        /// Obtains API client for authenticated access to internal queues.
        /// The client will access ProductConstructionService instance at the provided URI.
        /// </summary>
        /// <param name="accessToken">
        /// You can get the access token by logging in to your ProductConstructionService instance
        /// and proceeding to Profile page.
        /// </param>
        /// <param name="managedIdentityId">Managed Identity to use for the auth</param>
        /// <param name="disableInteractiveAuth">Whether to include interactive login flows</param>
        public static IProductConstructionServiceApi GetAuthenticated(
            string baseUri,
            string? accessToken,
            string? managedIdentityId,
            bool disableInteractiveAuth)
        {
            return new ProductConstructionServiceApi(new ProductConstructionServiceApiOptions(
                accessToken,
                managedIdentityId,
                disableInteractiveAuth));
        }

        /// <summary>
        /// Obtains API client for unauthenticated access to external queues.
        /// The client will access ProductConstructionService instance at the provided URI.
        /// </summary>
        /// <remarks>
        /// Attempt to access internal queues by such client will cause
        /// <see cref="ArgumentException"/> triggered by <c>SendAsync</c> call.
        /// </remarks>
        public static IProductConstructionServiceApi GetAnonymous(string baseUri)
        {
            return new ProductConstructionServiceApi(new ProductConstructionServiceApiOptions(new Uri(baseUri)));
        }
    }
}
