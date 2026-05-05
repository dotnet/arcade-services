// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using Microsoft.Extensions.Logging;

#nullable enable
namespace Microsoft.DotNet.ProductConstructionService.Client
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
        /// <param name="loggerFactory">Optional logger factory used by the interactive credential to emit progress messages.</param>
        /// <param name="clientName">Optional override for the <c>X-Client-Name</c> header.</param>
        /// <param name="clientVersion">Optional override for the <c>X-Client-Version</c> header.</param>
        public static IProductConstructionServiceApi GetAuthenticated(
            string? accessToken,
            string? managedIdentityId,
            bool disableInteractiveAuth,
            ILoggerFactory? loggerFactory = null,
            string? clientName = null,
            string? clientVersion = null)
        {
            return new ProductConstructionServiceApi(new ProductConstructionServiceApiOptions(
                accessToken,
                managedIdentityId,
                disableInteractiveAuth,
                loggerFactory,
                clientName,
                clientVersion));
        }

        /// <summary>
        /// Obtains API client for unauthenticated access to external queues.
        /// The client will access production ProductConstructionService instance.
        /// </summary>
        /// <remarks>
        /// Attempt to access internal queues by such client will cause
        /// <see cref="ArgumentException"/> triggered by <c>SendAsync</c> call.
        /// </remarks>
        public static IProductConstructionServiceApi GetAnonymous(
            string? clientName = null,
            string? clientVersion = null)
        {
            return new ProductConstructionServiceApi(new ProductConstructionServiceApiOptions(
                new Uri(""),
                credentials: null!,
                clientName,
                clientVersion));
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
        /// <param name="loggerFactory">Optional logger factory used by the interactive credential to emit progress messages.</param>
        /// <param name="clientName">Optional override for the <c>X-Client-Name</c> header.</param>
        /// <param name="clientVersion">Optional override for the <c>X-Client-Version</c> header.</param>
        public static IProductConstructionServiceApi GetAuthenticated(
            string baseUri,
            string? accessToken,
            string? managedIdentityId,
            bool disableInteractiveAuth,
            ILoggerFactory? loggerFactory = null,
            string? clientName = null,
            string? clientVersion = null)
        {
            return new ProductConstructionServiceApi(new ProductConstructionServiceApiOptions(
                baseUri,
                accessToken,
                managedIdentityId,
                disableInteractiveAuth,
                loggerFactory,
                clientName,
                clientVersion));
        }

        /// <summary>
        /// Obtains API client for unauthenticated access to external queues.
        /// The client will access ProductConstructionService instance at the provided URI.
        /// </summary>
        /// <remarks>
        /// Attempt to access internal queues by such client will cause
        /// <see cref="ArgumentException"/> triggered by <c>SendAsync</c> call.
        /// </remarks>
        public static IProductConstructionServiceApi GetAnonymous(
            string baseUri,
            string? clientName = null,
            string? clientVersion = null)
        {
            return new ProductConstructionServiceApi(new ProductConstructionServiceApiOptions(
                new Uri(baseUri),
                credentials: null!,
                clientName,
                clientVersion));
        }
    }
}
