// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Azure.Core;
using Azure.Core.Pipeline;

namespace Microsoft.DotNet.ProductConstructionService.Client
{
    /// <summary>
    /// Always-on pipeline policy that stamps client identity headers
    /// (<c>X-Client-Name</c>, <c>X-Client-Version</c>) on every request.
    /// The server uses these headers to require client identification and to
    /// enforce a minimum darc version.
    /// </summary>
    internal sealed class ClientIdentityHeaderPolicy : HttpPipelineSynchronousPolicy
    {
        public const string ClientNameHeader = "X-Client-Name";
        public const string ClientVersionHeader = "X-Client-Version";

        private readonly string _clientName;
        private readonly string _clientVersion;

        public ClientIdentityHeaderPolicy(string clientName, string clientVersion)
        {
            _clientName = clientName;
            _clientVersion = clientVersion;
        }

        public override void OnSendingRequest(HttpMessage message)
        {
            message.Request.Headers.SetValue(ClientNameHeader, _clientName);
            message.Request.Headers.SetValue(ClientVersionHeader, _clientVersion);
        }
    }
}
