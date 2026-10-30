// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.DotNet.Darc.Operations;

namespace Microsoft.DotNet.Darc.Options;

internal class AddDependencyCommandLineOptions : CommandLineOptions<AddDependencyOperation>
{
    public override string Verb => "add-dependency";

    public override string VerbDescription => "Add a new dependency to version files.";

    public string Name { get; set; }

    public string Type { get; set; }

    public string Version { get; set; }

    public string RepoUri { get; set; }

    public string Commit { get; set; }

    public bool Pinned { get; set; }

    public bool SkipProperty { get; set; }

    public string CoherentParentDependencyName { get; set; }

    public string RelativeBasePath { get; set; } = null;

    protected override void Configure()
    {
        Add<string>("--name", "-n", "Name of dependency to add.", value => Name = value, required: true);
        Add<string>("--type", "-t", "'toolset' or 'product'.", value => Type = value, required: true);
        Add<string>("--version", "-v", "Dependency version.", value => Version = value);
        Add<string>("--repo", "-r", "Repository where the dependency was built.", value => RepoUri = value, required: true);
        Add<string>("--commit", "-c", "SHA at which the dependency was produced.", value => Commit = value);
        Add<bool>("--pinned", "Whether the dependency is pinned or not.", value => Pinned = value);
        Add<bool>("--skip-property",
            "Whether to skip generating properties for this dependency in Version.Details.props. " +
            "Useful for dependencies that aren't referenced as NuGet packages (e.g., MSBuild SDK packages).",
            value => SkipProperty = value);
        Add<string>("--coherent-parent",
            "Restrict updates to this dependency based on version of a dependency from another repo. " +
            "See https://github.com/dotnet/arcade/blob/main/Documentation/DependencyDescriptionFormat.md#dependency-description-overview for more information.",
            value => CoherentParentDependencyName = value);
        Add<string>("--relative-base-path", "Used for VMR repos. Relative base path of the repo the dependency is being added to (e.g. src/sdk)", value => RelativeBasePath = value);
        base.Configure();
    }
}
