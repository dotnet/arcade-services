// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Azure.Core;
using Azure.Core.Pipeline;
using System;

namespace Microsoft.DotNet.Maestro.Client
{
    partial class MaestroApiOptions
    {
        private MaestroApiTokenCredential maestroApiTokenCredential;

        public MaestroApiOptions(MaestroApiTokenCredential maestroApiTokenCredential)
        {
            this.maestroApiTokenCredential = maestroApiTokenCredential;
        }

        partial void InitializeOptions()
        {
            if (Credentials != null)
            {
                AddPolicy(new BearerTokenAuthenticationPolicy(Credentials, Array.Empty<string>()), HttpPipelinePosition.PerCall);
            }
        }
    }
}
