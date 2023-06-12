// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Build.Utilities;
using System.Diagnostics;

namespace Microsoft.DotNet.Maestro.Tasks
{
    public class LaunchDebugger : Task
    {
        public override bool Execute()
        {
            Debugger.Launch();
            return true;
        }
    }
}
