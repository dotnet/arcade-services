// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.DotNet.Darc.Operations;

namespace Microsoft.DotNet.Darc.Options;

internal class AuthenticateCommandLineOptions : CommandLineOptions<AuthenticateOperation>
{
    public override string Verb => "authenticate";

    public override string VerbDescription => "Stores the VSTS and GitHub tokens required for remote operations.";

    public bool Clear { get; set; }

    protected override void Configure()
    {
        Add<bool>("--clear", "Clear any settings to defaults.", value => Clear = value);
        base.Configure();
    }
}
