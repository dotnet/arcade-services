// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using Azure.Core;
using Azure.Core.Pipeline;


namespace Microsoft.DotNet.Maestro.Client
{
    partial class MaestroApiOptions
    {
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
