// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using CommandLine;
using Microsoft.DotNet.Darc.Operations;
using Microsoft.DotNet.Darc.Operations.VirtualMonoRepo;

namespace Microsoft.DotNet.Darc.Options.VirtualMonoRepo;

[Verb("init", HelpText = "Initializes new repo(s) that haven't been synchronized into the VMR yet.")]
internal class InitializeCommandLineOptions : VmrCommandLineOptions
{
    public override Operation GetOperation() => new InitializeOperation(this);
}
