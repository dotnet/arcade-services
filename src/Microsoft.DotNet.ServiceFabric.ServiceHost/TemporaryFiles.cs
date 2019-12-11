// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Fabric;
using System.IO;

namespace Microsoft.DotNet.ServiceFabric.ServiceHost
{
    public class TemporaryFiles : IDisposable
    {
        private string _isolatedTempPath;

        public TemporaryFiles(ServiceContext context)
        {
            ServiceContext context1 = context;
            _isolatedTempPath = Environment.ExpandEnvironmentVariables($"%TEMP%\\{context1.ServiceTypeName}\\{context1.ReplicaOrInstanceId}");
        }

        public void Initialize()
        {
            // Do cleanup in the case of unhealthy exit last time.
            Cleanup();
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
                // Try and delete/clean it
                if (Directory.Exists(_isolatedTempPath))
                    Directory.Delete(_isolatedTempPath, true);
            }
            catch (IOException)
            {
                // It might not be here, it might be locked... there isn't really anything interesting to do, move on
                // this is just best effort to keep the machine clean
            }
        }
    }
}
