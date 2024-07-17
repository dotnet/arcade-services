// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using CommandLine;
using Microsoft.DotNet.Darc.Operations;

namespace Microsoft.DotNet.Darc.Options;

[Verb("authenticate", HelpText = "Stores the VSTS and GitHub tokens required for remote operations.")]
internal class AuthenticateCommandLineOptions : CommandLineOptions
{
    [Option("clear", HelpText = "Clear any settings to defaults.")]
    public bool Clear { get; set; }

    public override Type GetOperation()
    {
        return typeof(AuthenticateOperation);
    }
}
