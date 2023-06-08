// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Text;

namespace Microsoft.DotNet.DarcLib.Helpers;

public class ProcessExecutionResult
{
    public bool TimedOut { get; set; }
    public int ExitCode { get; set; }
    public bool Succeeded => !TimedOut && ExitCode == 0;
    public string StandardOutput { get; set; } = "";
    public string StandardError { get; set; } = "";

    public void ThrowIfFailed(string failureMessage)
    {
        if (!Succeeded)
        {
            throw new Exception(failureMessage + Environment.NewLine + this);
        }
    }

    public override string ToString()
    {
        var output = new StringBuilder();
        output.AppendLine($"Exit code: {ExitCode}");

        if (!string.IsNullOrEmpty(StandardOutput))
        {
            output.AppendLine($"Std out:{Environment.NewLine}{StandardOutput}{Environment.NewLine}");
        }

        if (!string.IsNullOrEmpty(StandardError))
        {
            output.AppendLine($"Std err:{Environment.NewLine}{StandardError}{Environment.NewLine}");
        }

        return output.ToString();
    }
}
