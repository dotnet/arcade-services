// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using CommandLine;
using Microsoft.DotNet.Darc.Operations;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.DotNet.Darc.Options;

[Verb("verify", HelpText = "Verify that the dependency information in the repository is correct.")]
internal class VerifyCommandLineOptions : CommandLineOptions
{
    public override Operation GetOperation(ServiceProvider sp) => ActivatorUtilities.CreateInstance<VerifyOperation>(sp, this);
}
