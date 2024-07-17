// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using CommandLine;
using Microsoft.DotNet.Darc.Operations;

namespace Microsoft.DotNet.Darc.Options;

[Verb("verify", HelpText = "Verify that the dependency information in the repository is correct.")]
internal class VerifyCommandLineOptions : CommandLineOptions
{
    public override Type GetOperation()
    {
        return typeof(VerifyOperation);
    }
}
