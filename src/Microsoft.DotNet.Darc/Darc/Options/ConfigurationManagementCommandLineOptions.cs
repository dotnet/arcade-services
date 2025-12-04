// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using CommandLine;
using Microsoft.DotNet.Darc.Operations;

namespace Microsoft.DotNet.Darc.Options;

internal interface IConfigurationManagementCommandLineOptions
{
    string ConfigurationBaseBranch { get; set; }
    string ConfigurationBranch { get; set; }
    string ConfigurationRepository { get; set; }
    bool Quiet { get; set; }
    bool NoPr { get; set; }
}

/// <summary>
/// Options tied to commands managing the configuration repository.
/// </summary>
internal abstract class ConfigurationManagementCommandLineOptions<T> : CommandLineOptions<T>, IConfigurationManagementCommandLineOptions where T : Operation
{
    private const string DefaultConfigurationRepository = "https://dev.azure.com/dnceng/internal/_git/dotnet-maestro-configuration-test";
    private const string DefaultBaseBranch = "production";

    [Option("configuration-repository", HelpText = "URI of the repository where configuration is stored in. Defaults to " + DefaultConfigurationRepository, Default = DefaultConfigurationRepository)]
    public string ConfigurationRepository { get; set; }

    [Option("configuration-branch", HelpText = "Branch of the configuration repository to update or leave out if it should be created.", Required = false)]
    public string ConfigurationBranch { get; set; }

    [Option("configuration-base-branch", HelpText = "Only applies when configuration branch is being created. Base branch to created the configuration branch off of. Defaults to " + DefaultBaseBranch, Required = false, Default = DefaultBaseBranch)]
    public string ConfigurationBaseBranch { get; set; }

    [Option("no-pr", HelpText = "Do not open a PR against the configuration repository (pushes the configuration branch only)", Default = false)]
    public bool NoPr { get; set; }

    [Option('q', "quiet", HelpText = "Non-interactive mode (requires all elements to be passed on the command line).")]
    public bool Quiet { get; set; }
}
