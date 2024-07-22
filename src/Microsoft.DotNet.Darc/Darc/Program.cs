// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using CommandLine;
using Microsoft.Arcade.Common;
using Microsoft.DotNet.Darc.Helpers;
using Microsoft.DotNet.Darc.Operations;
using Microsoft.DotNet.Darc.Options;
using Microsoft.DotNet.Darc.Options.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Logging;
using Maestro.Common.AzureDevOpsTokens;
using Maestro.Common;
using Microsoft.DotNet.Darc.Operations.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using Octokit;
using System.IO;
using Microsoft.Extensions.Logging.Abstractions;

namespace Microsoft.DotNet.Darc;

internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Contains("--debug"))
        {
            // Print header (version, sha, arguments)
            var version = FileVersionInfo.GetVersionInfo(Assembly.GetExecutingAssembly().Location);
            Console.WriteLine(
                $"[{version.ProductVersion} / {version.OriginalFilename}] " +
                "darc command issued: " + string.Join(' ', args));
        }

        Type[] options;
        if (args.FirstOrDefault() == "vmr")
        {
            options = GetVmrOptions();
            args = args.Skip(1).ToArray();
        }
        else
        {
            options = GetOptions();
        }

        return Parser.Default.ParseArguments(args, options)
                .MapResult(
                    (CommandLineOptions opts) => {
                        ServiceCollection services = new();

                        Configure(services, opts, args);

                        ServiceProvider provider = services.BuildServiceProvider();
                        opts.InitializeFromSettings(provider.GetRequiredService<ILogger>());

                        return RunOperation(opts, provider);
                    },
                    (errs => 1));
    }

    public static void Configure(ServiceCollection services, CommandLineOptions options, string[] args)
    {
        RegisterServices(services, options);
        RegisterOperations(services);

        if (args.FirstOrDefault() == "vmr")
        {
            RegisterVmrServices(services, (VmrCommandLineOptions)options);
            RegisterVMROperations(services);
        }
    }

    /// <summary>
    /// Runs the operation and calls dispose afterwards, returning the operation exit code.
    /// </summary>
    /// <param name="operation">Operation to run</param>
    /// <returns>Exit code of the operation</returns>
    /// <remarks>The primary reason for this is a workaround for an issue in the logging factory which
    /// causes it to not dispose the logging providers on process exit.  This causes missed logs, logs that end midway through
    /// and cause issues with the console coloring, etc.</remarks>
    private static int RunOperation(CommandLineOptions opts, ServiceProvider provider)
    {
        try
        {
            Operation operation = (Operation) provider.GetRequiredService(opts.GetOperation());

            return operation.ExecuteAsync().GetAwaiter().GetResult();
        }
        catch (Exception e)
        {
            Console.WriteLine("Unhandled exception encountered");
            Console.WriteLine(e);
            return Constants.ErrorCode;
        }
    }

    private static void RegisterServices(IServiceCollection services, CommandLineOptions options)
    {
        // Because the internal logging in DarcLib tends to be chatty and non-useful,
        // we remap the --verbose switch onto 'info', --debug onto highest level, and the
        // default level onto warning
        LogLevel level = LogLevel.Warning;
        if (options.Debug)
        {
            level = LogLevel.Debug;
        }
        else if (options.Verbose)
        {
            level = LogLevel.Information;
        }

        services ??= new ServiceCollection();
        services.AddLogging(b => b
            .AddConsole(o => o.FormatterName = CompactConsoleLoggerFormatter.FormatterName)
            .AddConsoleFormatter<CompactConsoleLoggerFormatter, SimpleConsoleFormatterOptions>()
            .SetMinimumLevel(level));

        services.AddSingleton(options);
        services.TryAddSingleton<IFileSystem, FileSystem>();
        services.TryAddSingleton<IRemoteFactory, RemoteFactory>();
        services.TryAddTransient<IProcessManager>(sp => ActivatorUtilities.CreateInstance<ProcessManager>(sp, options.GitLocation));
        services.TryAddSingleton(sp => RemoteFactory.GetBarClient(options, sp.GetRequiredService<ILogger<BarApiClient>>()));
        services.TryAddSingleton<IBasicBarClient>(sp => sp.GetRequiredService<IBarApiClient>());
        services.TryAddTransient<ILogger>(sp => sp.GetRequiredService<ILogger<Operation>>());
        services.TryAddTransient<ITelemetryRecorder, NoTelemetryRecorder>();
        services.TryAddTransient<IGitRepoFactory>(sp => ActivatorUtilities.CreateInstance<GitRepoFactory>(sp, Path.GetTempPath()));
        services.Configure<AzureDevOpsTokenProviderOptions>(o =>
        {
            o["default"] = new AzureDevOpsCredentialResolverOptions
            {
                Token = options.AzureDevOpsPat,
                FederatedToken = options.FederatedToken,
                DisableInteractiveAuth = options.IsCi,
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
        services.TryAddSingleton<IRemoteTokenProvider>(_ => new RemoteTokenProvider(options.AzureDevOpsPat, options.GitHubPat));
        services.TryAddSingleton<CommandLineOptions>(_ => options);
    }

    private static void RegisterVmrServices(ServiceCollection services, VmrCommandLineOptions vmrOptions)
    {
        string tmpPath = Path.GetFullPath(vmrOptions.TmpPath ?? Path.GetTempPath());
        LocalSettings localDarcSettings = null;

        var gitHubToken = vmrOptions.GitHubPat;
        var azureDevOpsToken = vmrOptions.AzureDevOpsPat;

        // Read tokens from local settings if not provided
        // We silence errors because the VMR synchronization often works with public repositories where tokens are not required
        if (gitHubToken == null || azureDevOpsToken == null)
        {
            try
            {
                localDarcSettings = LocalSettings.GetSettings(vmrOptions, NullLogger.Instance);
            }
            catch (DarcException)
            {
                // The VMR synchronization often works with public repositories where tokens are not required
            }

            gitHubToken ??= localDarcSettings?.GitHubToken;
            azureDevOpsToken ??= localDarcSettings?.AzureDevOpsToken;
        }

        services.AddVmrManagers(vmrOptions.GitLocation, vmrOptions.VmrPath, tmpPath, gitHubToken, azureDevOpsToken);
        services.TryAddTransient<IVmrScanner, VmrCloakedFileScanner>();
    }

    private static void RegisterOperations(ServiceCollection services)
    {
        services.TryAddSingleton<AddChannelOperation>();
        services.TryAddSingleton<AddDependencyOperation>();
        services.TryAddSingleton<AddDefaultChannelOperation>();
        services.TryAddSingleton<AddSubscriptionOperation>();
        services.TryAddSingleton<AddBuildToChannelOperation>();
        services.TryAddSingleton<AuthenticateOperation>();
        services.TryAddSingleton<CloneOperation>();
        services.TryAddSingleton<DefaultChannelStatusOperation>();
        services.TryAddSingleton<DeleteBuildFromChannelOperation>();
        services.TryAddSingleton<DeleteChannelOperation>();
        services.TryAddSingleton<DeleteDefaultChannelOperation>();
        services.TryAddSingleton<DeleteSubscriptionsOperation>();
        services.TryAddSingleton<GatherDropOperation>();
        services.TryAddSingleton<GetAssetOperation>();
        services.TryAddSingleton<GetBuildOperation>();
        services.TryAddSingleton<GetChannelOperation>();
        services.TryAddSingleton<GetChannelsOperation>();
        services.TryAddSingleton<GetDefaultChannelsOperation>();
        services.TryAddSingleton<GetDependenciesOperation>();
        services.TryAddSingleton<GetDependencyGraphOperation>();
        services.TryAddSingleton<GetDependencyFlowGraphOperation>();
        services.TryAddSingleton<GetHealthOperation>();
        services.TryAddSingleton<GetLatestBuildOperation>();
        services.TryAddSingleton<GetRepositoryMergePoliciesOperation>();
        services.TryAddSingleton<GetSubscriptionsOperation>();
        services.TryAddSingleton<SetRepositoryMergePoliciesOperation>();
        services.TryAddSingleton<SubscriptionsStatusOperation>();
        services.TryAddSingleton<TriggerSubscriptionsOperation>();
        services.TryAddSingleton<UpdateBuildOperation>();
        services.TryAddSingleton<UpdateDependenciesOperation>();
        services.TryAddSingleton<UpdateSubscriptionOperation>();
        services.TryAddSingleton<VerifyOperation>();
        services.TryAddSingleton<SetGoalOperation>();
        services.TryAddSingleton<GetGoalOperation>();
    }

    private static void RegisterVMROperations(ServiceCollection services)
    {
        services.TryAddSingleton<BackflowOperation>();
        services.TryAddSingleton<CloakedFileScanOperation>();
        services.TryAddSingleton<ForwardFlowOperation>();
        services.TryAddSingleton<GenerateTpnOperation>();
        services.TryAddSingleton<GetRepoVersionOperation>();
        services.TryAddSingleton<InitializeOperation>();
        services.TryAddSingleton<PushOperation>();
        services.TryAddSingleton<UpdateOperation>();
    }

    // This order will mandate the order in which the commands are displayed if typing just 'darc'
    // so keep these sorted.
    private static Type[] GetOptions() =>
    [
        typeof(AddChannelCommandLineOptions),
        typeof(AddDependencyCommandLineOptions),
        typeof(AddDefaultChannelCommandLineOptions),
        typeof(AddSubscriptionCommandLineOptions),
        typeof(AddBuildToChannelCommandLineOptions),
        typeof(AuthenticateCommandLineOptions),
        typeof(CloneCommandLineOptions),
        typeof(DefaultChannelStatusCommandLineOptions),
        typeof(DeleteBuildFromChannelCommandLineOptions),
        typeof(DeleteChannelCommandLineOptions),
        typeof(DeleteDefaultChannelCommandLineOptions),
        typeof(DeleteSubscriptionsCommandLineOptions),
        typeof(GatherDropCommandLineOptions),
        typeof(GetAssetCommandLineOptions),
        typeof(GetBuildCommandLineOptions),
        typeof(GetChannelCommandLineOptions),
        typeof(GetChannelsCommandLineOptions),
        typeof(GetDefaultChannelsCommandLineOptions),
        typeof(GetDependenciesCommandLineOptions),
        typeof(GetDependencyGraphCommandLineOptions),
        typeof(GetDependencyFlowGraphCommandLineOptions),
        typeof(GetHealthCommandLineOptions),
        typeof(GetLatestBuildCommandLineOptions),
        typeof(GetRepositoryMergePoliciesCommandLineOptions),
        typeof(GetSubscriptionsCommandLineOptions),
        typeof(SetRepositoryMergePoliciesCommandLineOptions),
        typeof(SubscriptionsStatusCommandLineOptions),
        typeof(TriggerSubscriptionsCommandLineOptions),
        typeof(UpdateBuildCommandLineOptions),
        typeof(UpdateDependenciesCommandLineOptions),
        typeof(UpdateSubscriptionCommandLineOptions),
        typeof(VerifyCommandLineOptions),
        typeof(SetGoalCommandLineOptions),
        typeof(GetGoalCommandLineOptions),
    ];

    // These are under the "vmr" subcommand
    private static Type[] GetVmrOptions() =>
    [
        typeof(InitializeCommandLineOptions),
        typeof(UpdateCommandLineOptions),
        typeof(BackflowCommandLineOptions),
        typeof(ForwardFlowCommandLineOptions),
        typeof(GenerateTpnCommandLineOptions),
        typeof(CloakedFileScanOptions),
        typeof(GetRepoVersionCommandLineOptions),
        typeof(VmrPushCommandLineOptions)
    ];
}
