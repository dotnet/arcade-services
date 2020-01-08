// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Fabric;
using System.IO;
using Microsoft.Extensions.Logging;

namespace Microsoft.DotNet.ServiceFabric.ServiceHost
{
    public class TemporaryFiles : IDisposable
    {
        private readonly ILogger<TemporaryFiles> _logger;
        private readonly string _isolatedTempPath;

        public TemporaryFiles(
            ServiceContext context,
            ILogger<TemporaryFiles> logger)
        {
            _logger = logger;
            _isolatedTempPath = Path.Combine(
                Path.GetTempPath(),
                context.ServiceTypeName,
                context.ReplicaOrInstanceId.ToString()
            );
        }

        public void Initialize()
        {
            // Do cleanup in the case of unhealthy exit last time.
            Cleanup();
            _logger.LogTrace("Creating isolated temp directory at {path}", _isolatedTempPath);
            Directory.CreateDirectory(_isolatedTempPath);
        }

        public string GetFilePath(params string[] parts)
        {
            return Path.Combine(_isolatedTempPath, Path.Combine(parts));
        }

        public void Dispose()
        {
            Cleanup();
        }

        private void Cleanup()
        {
            try
            {
                if (!Directory.Exists(_isolatedTempPath))
                {
                    return;
                }

                _logger.LogTrace("Temporary files found, cleaning up {path}", _isolatedTempPath);
                Directory.Delete(_isolatedTempPath, true);
            }
            catch (IOException e)
            {
                // It might not be here, it might be locked... there isn't really anything interesting to do, move on
                // this is just best effort to keep the machine clean
                _logger.LogError(e, "Failed to clean up temporary directory {path}", _isolatedTempPath);
            }
        }
    }
}
