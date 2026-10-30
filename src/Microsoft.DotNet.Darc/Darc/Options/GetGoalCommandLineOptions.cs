// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.DotNet.Darc.Operations;

namespace Microsoft.DotNet.Darc.Options;

internal class GetGoalCommandLineOptions : CommandLineOptions<GetGoalOperation>
{
    public override string Verb => "get-goal";

    public override string VerbDescription => "Gets Goal in minutes for a Definition in a Channel";

    public string Channel { get; set; }

    public int DefinitionId { get; set; }

    protected override void Configure()
    {
        Add<string>("--channel", "-c", "Name of channel Eg : .Net Core 5 Dev ", value => Channel = value, required: true);
        Add<int>("--definition-id", "-d", "Azure DevOps Definition Id.", value => DefinitionId = value, required: true);
        base.Configure();
    }
}
