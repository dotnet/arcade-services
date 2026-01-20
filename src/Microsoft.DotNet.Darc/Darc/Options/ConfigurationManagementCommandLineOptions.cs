// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using CommandLine;
using Microsoft.DotNet.Darc.Operations;
using Microsoft.DotNet.DarcLib.ConfigurationRepository;
using Microsoft.DotNet.MaestroConfiguration.Client;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.DotNet.Darc.Options;

internal interface IConfigurationManagementCommandLineOptions
{
    string ConfigurationBaseBranch { get; set; }
    string ConfigurationBranch { get; set; }
    string ConfigurationRepository { get; set; }
    string ConfigurationFilePath { get; set; }
    bool NoPr { get; set; }
}

/// <summary>
/// Options tied to commands managing the configuration repository.
/// </summary>
internal abstract class ConfigurationManagementCommandLineOptions<T> : CommandLineOptions<T>, IConfigurationManagementCommandLineOptions where T : Operation
{
    private const string DefaultConfigurationRepository = "https://dev.azure.com/dnceng/internal/_git/maestro-configuration";
    private const string DefaultConfigurationBaseBranch = "production";

    [Option("configuration-repository", HelpText = "URI of the repository where configuration is stored in. Defaults to " + DefaultConfigurationRepository, Default = DefaultConfigurationRepository)]
    public string ConfigurationRepository { get; set; }

    [Option("configuration-branch", HelpText = "Branch of the configuration repository to make the change on. Leave null to create a new one.", Required = false)]
    public string ConfigurationBranch { get; set; }

    [Option("configuration-base-branch", HelpText = "Base branch to create the configuration branch off of (if it doesn't exist yet). Defaults to production", Default = DefaultConfigurationBaseBranch, Required = false)]
    public string ConfigurationBaseBranch { get; set; }

    [Option("configuration-file", HelpText = "Optional override of the target file the configuration will be stored in, e.g. configuration/channels/net-11-preview-3.yml", Required = false)]
    public string ConfigurationFilePath { get; set; }

    [Option("no-pr", HelpText = "Do not open a PR against the configuration repository (push the configuration branch only).", Default = false)]
    public bool NoPr { get; set; }

    public override IServiceCollection RegisterServices(IServiceCollection services)
    {
        if (!Verbose && !Debug)
        {
            // Force verbose output for these commands
            Verbose = true;
        }
        services.AddSingleton<IGitRepoFactory, DarcLib.ConfigurationRepository.GitRepoFactory>();
        services.AddSingleton<IConfigurationRepositoryManager, ConfigurationRepositoryManager>();
        return base.RegisterServices(services);
    }

    public ConfigurationRepositoryOperationParameters ToConfigurationRepositoryOperationParameters()
    {
        return new ConfigurationRepositoryOperationParameters
        {
            RepositoryUri = ConfigurationRepository,
            ConfigurationBranch = ConfigurationBranch,
            ConfigurationBaseBranch = ConfigurationBaseBranch,
            DontOpenPr = NoPr,
            ConfigurationFilePath = ConfigurationFilePath,
        }; 
    }
}
