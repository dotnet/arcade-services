// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;
using System.IO;


namespace Microsoft.DotNet.Maestro.Tasks
{
    public interface IDirectoryProxy
    {
        void CreateDirectory(string path);
        bool Exists(string path);
        ICollection<string> GetFiles(string path, string searchPattern, SearchOption searchOption);
        void Move(string sourceDirName, string destDirName);
    }
}
