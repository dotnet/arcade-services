// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;

namespace Microsoft.DncEng.CommandLineLib
{
    public class FailWithExitCodeException : Exception
    {
        public int ExitCode { get; }
        public bool ShowMessage { get; }

        public FailWithExitCodeException(int exitCode, string message = null) : base(message)
        {
            ShowMessage = !string.IsNullOrEmpty(message);
            ExitCode = exitCode;
        }
    }
}
