// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.DotNet.Maestro.Client;

namespace Microsoft.DotNet.Internal.Maestro
{
    public sealed class MaestroApiClientFactory : IMaestroApiClientFactory
    {
        public MaestroApiClientFactory() { }

        public IMaestroApi CreateMaestroClient(string apiToken, string baseUrl)
        {
            return ApiFactory.GetAuthenticated(apiToken, baseUrl);
        }
    }
}