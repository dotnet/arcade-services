// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.DotNet.Darc.Operations;

namespace Microsoft.DotNet.Darc.Options;

internal class UpdateBuildCommandLineOptions : CommandLineOptions<UpdateBuildOperation>
{
    public override string Verb => "update-build";

    public override string VerbDescription => "Update a build with new information.";

    public int Id { get; set; }

    public bool Released { get; set; }

    public bool NotReleased { get; set; }

    protected override void Configure()
    {
        Add<int>("--id", "Build id.", value => Id = value, required: true);
        Add<bool>("--released", "Set the build to being 'released'.", value => Released = value);
        Add<bool>("--not-released", "Set the build to 'not released'.", value => NotReleased = value);
        base.Configure();
    }
}
