// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.IO;
using System;

namespace Microsoft.DotNet.DarcLib.Helpers;

public static class LocalGit
{
    public static string GetPathToLocalGit()
    {
        var gitExePath = Path.Join(AppContext.BaseDirectory, "git-portable", "bin", "git.exe");
        if (!File.Exists(gitExePath))
        {
            throw new InvalidOperationException(
                $"Portable git not found at path {gitExePath}, the build needs to be configured to publish it inside the service package.");
        }

        return gitExePath;
    }
}
