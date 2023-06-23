// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.IO;
using System.Threading;

namespace Maestro.ScenarioTests
{
    public class TemporaryDirectory : IDisposable
    {
        public static TemporaryDirectory Get()
        {
            string dir = Path.Join(Path.GetTempPath(), Path.GetRandomFileName());
            System.IO.Directory.CreateDirectory(dir);
            return new TemporaryDirectory(dir);
        }

        public string Directory { get; }

        private TemporaryDirectory(string dir)
        {
            Directory = dir;
        }

        public void Dispose()
        {
            try
            {
                System.IO.Directory.Delete(Directory, true);
            }
            catch (UnauthorizedAccessException)
            {
                Thread.Sleep(5000);
                try
                {
                    System.IO.Directory.Delete(Directory, true);
                }
                catch (UnauthorizedAccessException)
                {
                    // We tried, don't fail the test.
                }
            }
        }
    }
}
