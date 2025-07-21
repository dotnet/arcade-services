// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.IO;
using CommandLine;
using Maestro.Common;
using Maestro.Common.AzureDevOpsTokens;
using Microsoft.DotNet.Darc.Helpers;
using Microsoft.DotNet.Darc.Operations;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using ProductConstructionService.Common;

namespace Microsoft.DotNet.Darc.Options;

public abstract class CommandLineOptions<T> : CommandLineOptions where T : Operation
{
    public override Operation GetOperation(ServiceProvider sp)
    {
        return ActivatorUtilities.CreateInstance<T>(sp, this);
    }
}

public abstract class CommandLineOptions : ICommandLineOptions
{
    [Option('p', "password",
        HelpText = "Token used to authenticate to BAR. When omitted, Azure CLI or an interactive browser login flow are used.")]
    [RedactFromLogging]
    public string BuildAssetRegistryToken { get; set; } = null;

    [Option("github-pat", HelpText = "Token used to authenticate GitHub.")]
    [RedactFromLogging]
    public string GitHubPat { get; set; }

    [Option("azdev-pat", HelpText = "Optional token used to authenticate to Azure DevOps. When not provided, local credentials are used.")]
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
    public DarcOutputType OutputFormat
    {
        get
        {
            return _outputFormat;
        }
        set
        {
            _outputFormat = value;
            if (!IsOutputFormatSupported())
            {
                throw new ArgumentException($"Output format {_outputFormat} is not supported by operation ${GetType().Name}");
            }
        }
    }

    private DarcOutputType _outputFormat;

    /// <summary>
    /// Designates that darc is run from a CI environment.
    /// Some behaviours are disabled in CI such as the interactive browser sign-in to Maestro (and AzureCLI/federated token is used).
    /// </summary>
    [Option("ci", HelpText = "Designates that darc is run from a CI environment with some features disabled (e.g. interactive browser sign-in to Maestro)")]
    public bool IsCi { get; set; }

    public abstract Operation GetOperation(ServiceProvider sp);

    public IRemoteTokenProvider GetRemoteTokenProvider()
        => new RemoteTokenProvider(GetAzdoTokenProvider(), GetGitHubTokenProvider());

    public IAzureDevOpsTokenProvider GetAzdoTokenProvider()
    {
        var azdoOptions = new AzureDevOpsTokenProviderOptions
        {
            ["default"] = new AzureDevOpsCredentialResolverOptions
            {
                Token = AzureDevOpsPat,
                DisableInteractiveAuth = IsCi,
            }
        };
        return AzureDevOpsTokenProvider.FromStaticOptions(azdoOptions);
    }

    public IRemoteTokenProvider GetGitHubTokenProvider() => new ResolvedTokenProvider(GitHubPat);

    public void InitializeFromSettings(ILogger logger)
    {
        var localSettings = LocalSettings.GetSettings(this, logger);
        AzureDevOpsPat ??= localSettings.AzureDevOpsToken;
        GitHubPat ??= localSettings.GitHubToken;
        BuildAssetRegistryBaseUri ??= localSettings.BuildAssetRegistryBaseUri;
    }

    /// <summary>
    ///  Indicates whether the requested output format is supported.
    /// </summary>
    public virtual bool IsOutputFormatSupported()
        => OutputFormat switch
        {
            DarcOutputType.text => true,
            _ => false
        };

    public virtual IServiceCollection RegisterServices(IServiceCollection services)
    {
        // Because the internal logging in DarcLib tends to be chatty and non-useful,
        // we remap the --verbose switch onto 'info', --debug onto highest level, and the
        // default level onto warning
        LogLevel level = LogLevel.Warning;
        if (Debug)
        {
            level = LogLevel.Debug;
        }
        else if (Verbose)
        {
            level = LogLevel.Information;
        }

        services ??= new ServiceCollection();
        services.AddLogging(b => b
            .AddConsole(o => o.FormatterName = CompactConsoleLoggerFormatter.FormatterName)
            .AddConsoleFormatter<CompactConsoleLoggerFormatter, SimpleConsoleFormatterOptions>()
            .SetMinimumLevel(level));

        services.TryAddSingleton<IFileSystem, FileSystem>();
        services.TryAddSingleton<IRemoteFactory, RemoteFactory>();
        services.TryAddSingleton<IVersionDetailsParser, VersionDetailsParser>();
        services.TryAddSingleton<IAssetLocationResolver, AssetLocationResolver>();
        services.TryAddTransient<IProcessManager>(sp => new ProcessManager(sp.GetRequiredService<ILogger<ProcessManager>>(), GitLocation));
        services.TryAddSingleton<IBarApiClient>(sp => new BarApiClient(
            BuildAssetRegistryToken,
            managedIdentityId: null,
            disableInteractiveAuth: IsCi,
            BuildAssetRegistryBaseUri));
        services.TryAddSingleton<IBasicBarClient>(sp => sp.GetRequiredService<IBarApiClient>());
        services.TryAddTransient<ICoherencyUpdateResolver, CoherencyUpdateResolver>();
        services.TryAddTransient<ILogger>(sp => sp.GetRequiredService<ILogger<Operation>>());
        services.TryAddTransient<ITelemetryRecorder, NoTelemetryRecorder>();
        services.TryAddTransient<IGitRepoFactory>(sp => ActivatorUtilities.CreateInstance<GitRepoFactory>(sp, Path.GetTempPath()));
        services.Configure<AzureDevOpsTokenProviderOptions>(o =>
        {
            o["default"] = new AzureDevOpsCredentialResolverOptions
            {
                Token = AzureDevOpsPat,
                DisableInteractiveAuth = IsCi,
            };
        });
        services.TryAddSingleton<IAzureDevOpsTokenProvider, AzureDevOpsTokenProvider>();
        services.TryAddSingleton(s =>
            new AzureDevOpsClient(
                s.GetRequiredService<IAzureDevOpsTokenProvider>(),
                s.GetRequiredService<IProcessManager>(),
                s.GetRequiredService<ILogger>())
        );
        services.TryAddSingleton<IAzureDevOpsClient>(s =>
            s.GetRequiredService<AzureDevOpsClient>()
        );
        services.TryAddSingleton<IRemoteTokenProvider>(_ => new RemoteTokenProvider(AzureDevOpsPat, GitHubPat));
        services.TryAddSingleton<ICommandLineOptions>(_ => this);
        // Add add an empty VmrInfo that won't actually be used in non VMR commands
        services.TryAddSingleton<ISourceMappingParser, SourceMappingParser>();
        services.TryAddSingleton<IVmrInfo>(sp =>
        {
            return new VmrInfo(string.Empty, string.Empty);
        });
        services.TryAddSingleton<IRedisCacheClient, NoOpRedisClient>();

        return services;
    }
}
