// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

namespace DotNet.Grafana
{
    public class Credentials
    {
        /// <summary>
        /// Store credentials for Grafana API access
        /// </summary>
        /// <param name="token">Token for bearer auth</param>
        public Credentials(string token)
        {
            Token = token;
        }

        public string Token { get; }
    }
}
