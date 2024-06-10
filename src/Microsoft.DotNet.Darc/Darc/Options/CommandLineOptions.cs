// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using CommandLine;
using Microsoft.DotNet.Darc.Operations;
using Microsoft.DotNet.DarcLib;

namespace Microsoft.DotNet.Darc.Options;

public abstract class CommandLineOptions : ICommandLineOptions
{
    [Option('p', "password",
        HelpText = "Token used to authenticate to BAR. If it or the federated token are omitted, auth falls back to Azure CLI or an interactive browser login flow.")]
    [RedactFromLogging]
    public string BuildAssetRegistryToken { get; set; }

    [Option('t', "federated-token",
        HelpText = "Federated credentials token used to authenticate to BAR. If it or the generic token are omitted, auth falls back to Azure CLI or an interactive browser login flow.")]
    [RedactFromLogging]
    public string FederatedToken { get; set; }

    [Option("github-pat", HelpText = "Token used to authenticate GitHub.")]
    [RedactFromLogging]
    public string GitHubPat { get; set; }

    [Option("azdev-pat", HelpText = "Token used to authenticate to Azure DevOps.")]
    [RedactFromLogging]
    public string AzureDevOpsPat { get; set; }

    [Option("bar-uri", HelpText = "URI of the build asset registry service to use.")]
    public string BuildAssetRegistryBaseUri { get; set; }

    [Option("verbose", HelpText = "Turn on verbose output.")]
    public bool Verbose { get; set; }

    [Option("debug", HelpText = "Turn on debug output.")]
    public bool Debug { get; set; }

    [Option("git-location", Default = "git", HelpText = "Location of git executable used for internal commands.")]
    [RedactFromLogging]
    public string GitLocation { get; set; }

    [Option("output-format", Default = DarcOutputType.text,
        HelpText = "Desired output type of darc. Valid values are 'json' and 'text'. Case sensitive.")]
    public DarcOutputType OutputFormat { get; set; }

    /// <summary>
    /// When true, Darc authenticates against Maestro using an Azure CLI flow.
    /// </summary>
    [Option("ci", HelpText = "Disable interactive sign-in to Maestro API for CI runs.")]
    public bool IsCi { get; set; }

    public abstract Operation GetOperation();

    public RemoteConfiguration GetRemoteConfiguration()
    {
        return new RemoteConfiguration(GitHubPat, AzureDevOpsPat);
    }
}
