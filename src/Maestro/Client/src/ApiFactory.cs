// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;

namespace Microsoft.DotNet.Maestro.Client
{
    public static class ApiFactory
    {
        /// <summary>
        /// Obtains API client for authenticated access to internal queues.
        /// The client will access production Maestro instance.
        /// </summary>
        /// <param name="accessToken">
        /// You can get the access token by logging in to your Maestro instance
        /// and proceeding to Profile page.
        /// </param>
        public static IMaestroApi GetAuthenticated(string accessToken)
        {
            return new MaestroApi(new MaestroApiOptions(new MaestroApiTokenCredential(accessToken)));
        }

        /// <summary>
        /// Obtains API client for unauthenticated access to external queues.
        /// The client will access production Maestro instance.
        /// </summary>
        /// <remarks>
        /// Attempt to access internal queues by such client will cause
        /// <see cref="ArgumentException"/> triggered by <c>SendAsync</c> call.
        /// </remarks>
        public static IMaestroApi GetAnonymous()
        {
            return new MaestroApi(new MaestroApiOptions());
        }

        /// <summary>
        /// Obtains API client for authenticated access to internal queues.
        /// The client will access Maestro instance at the provided URI.
        /// </summary>
        /// <param name="accessToken">
        /// You can get the access token by logging in to your Maestro instance
        /// and proceeding to Profile page.
        /// </param>
        public static IMaestroApi GetAuthenticated(string baseUri, string accessToken)
        {
            return new MaestroApi(new MaestroApiOptions(new Uri(baseUri), new MaestroApiTokenCredential(accessToken)));
        }

        /// <summary>
        /// Obtains API client for unauthenticated access to external queues.
        /// The client will access Maestro instance at the provided URI.
        /// </summary>
        /// <remarks>
        /// Attempt to access internal queues by such client will cause
        /// <see cref="ArgumentException"/> triggered by <c>SendAsync</c> call.
        /// </remarks>
        public static IMaestroApi GetAnonymous(string baseUri)
        {
            return new MaestroApi(new MaestroApiOptions(new Uri(baseUri)));
        }
    }
}
