// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using CommandLine;
using Microsoft.DotNet.Darc.Operations;
using Microsoft.DotNet.Darc.Operations.VirtualMonoRepo;

namespace Microsoft.DotNet.Darc.Options.VirtualMonoRepo;

[Verb("scan", HelpText = "Scans the VMR, checking if it contains any cloacked files")]
internal class ScanCommandLineOptions : VmrCommandLineOptions
{
    public ScanCommandLineOptions()
    {
    }

    public override Operation GetOperation() => new ScanOperation(this);
}
