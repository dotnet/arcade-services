// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.DotNet.Darc.Operations;

namespace Microsoft.DotNet.Darc.Options;

internal class AddDefaultChannelCommandLineOptions : ConfigurationManagementCommandLineOptions<AddDefaultChannelOperation>
{
    public override string Verb => "add-default-channel";

    public override string VerbDescription => "Add a channel that a build of a branch+repository is automatically applied to.";

    public string Channel { get; set; }

    public string Branch { get; set; }

    public string Repository { get; set; }

    public bool UseBranchAsRegex { get; set; }

    public bool NoConfirmation { get; set; }

    protected override void Configure()
    {
        Add<string>("--channel", "Name of channel that a build of 'branch' and 'repo' should be applied to.", value => Channel = value, required: true);
        Add<string>("--branch", "Builds of 'repo' on this branch will be automatically applied to 'channel'.  Use with '--regex' to match on multiple branch names", value => Branch = value, required: true);
        Add<string>("--repo", "Builds of this repo on 'branch' will be automatically applied to 'channel'", value => Repository = value, required: true);
        Add<bool>("--regex", "If specified, the value of the 'branch' option will be treated as a regular expression for matching branch names.", value => UseBranchAsRegex = value, defaultValue: () => false);
        Add<bool>("--quiet", "-q", "Do not prompt if the target repository/branch does not exist.", value => NoConfirmation = value);
        base.Configure();
    }
}
