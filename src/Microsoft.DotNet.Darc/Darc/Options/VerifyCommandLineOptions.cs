// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.DotNet.Darc.Operations;

namespace Microsoft.DotNet.Darc.Options;

internal class VerifyCommandLineOptions : CommandLineOptions<VerifyOperation>
{
    public override string Verb => "verify";

    public override string VerbDescription => "Verify that the dependency information in the repository is correct.";
}
