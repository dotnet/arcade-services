// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.DotNet.Darc.Options;
using Microsoft.DotNet.DarcLib;
using Microsoft.Extensions.Logging;
using System;

namespace Microsoft.DotNet.Darc.Helpers
{
    internal class RemoteFactory : IRemoteFactory
    {
        CommandLineOptions _options;

        public RemoteFactory(CommandLineOptions options)
        {
            _options = options;
        }

        public IRemote GetRemote(string repoUrl, ILogger logger)
        {
            DarcSettings darcSettings = LocalSettings.GetDarcSettings(_options, logger, repoUrl);
            return new Remote(darcSettings, logger);
        }
    }
}
