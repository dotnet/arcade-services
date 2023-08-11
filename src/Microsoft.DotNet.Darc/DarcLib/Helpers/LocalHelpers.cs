// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;

#nullable enable
namespace Microsoft.DotNet.DarcLib.Helpers;

public static class LocalHelpers
{
    public static string GetRootDir(string gitLocation, ILogger logger)
    {
        return ExecuteCommand(gitLocation, "rev-parse --show-toplevel", logger)
            ?? throw new Exception("Root directory of the repo was not found. Check that git is installed and that you are in a folder which is a git repo (.git folder should be present).");
    }

    public static string? ExecuteCommand(string command, string arguments, ILogger logger, string? workingDirectory = null)
    {
        try
        {
            var processInfo = new ProcessStartInfo
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                FileName = command ?? throw new ArgumentException("Executable command must be non-empty"),
                CreateNoWindow = true,
                WorkingDirectory = workingDirectory ?? Environment.CurrentDirectory
            };

            using (var process = new Process())
            {
                process.StartInfo = processInfo;
                process.StartInfo.Arguments = arguments;
                process.Start();

                string? output = process.StandardOutput.ReadToEnd().Trim();

                process.WaitForExit();

                return output;
            }
        }
        catch (Exception exc)
        {
            logger.LogWarning($"Something failed while trying to execute '{command} {arguments}'. Exception: {exc.Message}");
            return null;
        }
    }
}
