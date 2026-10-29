// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Diagnostics.CodeAnalysis;
using Microsoft.DotNet.Darc.Operations;
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
    string GetOrGenerateConfigurationBranch();
}

/// <summary>
/// Options tied to commands managing the configuration repository.
/// </summary>
internal abstract class ConfigurationManagementCommandLineOptions<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T> : CommandLineOptions<T>, IConfigurationManagementCommandLineOptions where T : Operation
{
    private const string DefaultConfigurationRepository = "https://dev.azure.com/dnceng/internal/_git/maestro-configuration";
    private const string DefaultConfigurationBaseBranch = "production";

    private string _generatedBranchName;

    public string ConfigurationRepository { get; set; }

    public string ConfigurationBranch { get; set; }

    public string ConfigurationBaseBranch { get; set; }

    public string ConfigurationFilePath { get; set; }

    public bool NoPr { get; set; }

    protected override void Configure()
    {
        Add<string>("--configuration-repository", "URI of the repository where configuration is stored in. Defaults to " + DefaultConfigurationRepository, value => ConfigurationRepository = value, defaultValue: () => DefaultConfigurationRepository);
        Add<string>("--configuration-branch", "Branch of the configuration repository to make the change on. Leave null to create a new one.", value => ConfigurationBranch = value);
        Add<string>("--configuration-base-branch", "Base branch to create the configuration branch off of (if it doesn't exist yet). Defaults to production", value => ConfigurationBaseBranch = value, defaultValue: () => DefaultConfigurationBaseBranch);
        Add<string>("--configuration-file", "Optional override of the target file the configuration will be stored in, e.g. configuration/channels/net-11-preview-3.yml", value => ConfigurationFilePath = value);
        Add<bool>("--no-pr", "Do not open a PR against the configuration repository (push the configuration branch only).", value => NoPr = value, defaultValue: () => false);
        base.Configure();
    }

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

    /// <summary>
    /// Gets the effective configuration branch: the user-provided one, or a newly generated one
    /// (generated once and cached for reuse across multiple operations in the same command).
    /// </summary>
    public string GetOrGenerateConfigurationBranch()
        => ConfigurationBranch ?? (_generatedBranchName ??= GenerateBatchBranchName());

    public ConfigurationRepositoryOperationParameters ToConfigurationRepositoryOperationParameters()
    {
        return new ConfigurationRepositoryOperationParameters
        {
            RepositoryUri = ConfigurationRepository,
            ConfigurationBranch = GetOrGenerateConfigurationBranch(),
            ConfigurationBaseBranch = ConfigurationBaseBranch,
            DontOpenPr = NoPr,
            ConfigurationFilePath = ConfigurationFilePath,
        }; 
    }

    /// <summary>
    /// Generates a unique configuration branch name based on the base branch.
    /// Used when multiple operations need to share a single branch.
    /// </summary>
    public string GenerateBatchBranchName()
        => $"darc/{ConfigurationBaseBranch}-{Guid.NewGuid().ToString("N")[..8]}";
}
