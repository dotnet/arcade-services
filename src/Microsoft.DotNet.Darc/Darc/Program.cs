// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using CommandLine;
using Microsoft.DotNet.Darc.Operations;
using Microsoft.DotNet.Darc.Options;
using Microsoft.DotNet.Darc.Options.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

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
            args = [.. args.Skip(1)];
        }
        else
        {
            options = GetOptions();
        }

        // If we're using the 'get-asset' command, we don't want to interfere with the --version parameter it has
        bool useAutoVersion = args.FirstOrDefault() != "get-asset";

        Parser parser = new(settings => { settings.AutoVersion = useAutoVersion; settings.HelpWriter = Console.Error; });

        return parser.ParseArguments(args, options)
                .MapResult(
                    (CommandLineOptions opts) => {
                        ServiceCollection services = new();

                        opts.RegisterServices(services);

                        using ServiceProvider provider = services.BuildServiceProvider();
                        opts.InitializeFromSettings(provider.GetRequiredService<ILogger>());

                        var ret = RunOperation(opts, provider);

                        var logger = provider.GetRequiredService<ILogger>();
                        var comments = provider.GetRequiredService<ICommentCollector>().GetComments();

                        foreach (var comment in comments)
                        {
                            switch (comment.Type)
                            {
                                case CommentType.Caution:
                                case CommentType.Warning:
                                    logger.LogWarning(comment.Text);
                                    break;
                                case CommentType.Information:
                                    logger.LogInformation(comment.Text);
                                    break;
                            }
                        }

                        return ret;
                    },
                    (errs => 1));
    }

    /// <summary>
    /// Runs the operation and calls dispose afterwards, returning the operation exit code.
    /// </summary>
    /// <param name="operation">Operation to run</param>
    /// <returns>Exit code of the operation</returns>
    /// <remarks>The primary reason for this is a workaround for an issue in the logging factory which
    /// causes it to not dispose the logging providers on process exit.  This causes missed logs, logs that end midway through
    /// and cause issues with the console coloring, etc.</remarks>
    private static int RunOperation(CommandLineOptions opts, ServiceProvider sp)
    {
        try
        {
            Operation operation = opts.GetOperation(sp);

            return operation.ExecuteAsync().GetAwaiter().GetResult();
        }
        catch (Exception e)
        {
            Console.WriteLine("Unhandled exception encountered");
            Console.WriteLine(e);
            return Constants.ErrorCode;
        }
    }

    // This order will mandate the order in which the commands are displayed if typing just 'darc'
    // so keep these sorted.
    public static Type[] GetOptions() =>
    [
        typeof(AddChannelCommandLineOptions),
        typeof(AddDependencyCommandLineOptions),
        typeof(AddDefaultChannelCommandLineOptions),
        typeof(AddSubscriptionCommandLineOptions),
        typeof(AddBuildToChannelCommandLineOptions),
        typeof(AuthenticateCommandLineOptions),
        typeof(LoginCommandLineOptions),
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
        typeof(UpdateChannelCommandLineOptions),
        typeof(UpdateDependenciesCommandLineOptions),
        typeof(UpdateSubscriptionCommandLineOptions),
        typeof(VerifyCommandLineOptions),
        typeof(SetGoalCommandLineOptions),
        typeof(GetGoalCommandLineOptions),
    ];

    // These are under the "vmr" subcommand
    public static Type[] GetVmrOptions() =>
    [
        typeof(AddRepoCommandLineOptions),
        typeof(RemoveRepoCommandLineOptions),
        typeof(BackflowCommandLineOptions),
        typeof(ForwardFlowCommandLineOptions),
        typeof(ResolveConflictCommandLineOptions),
        typeof(CherryPickCommandLineOptions),
        typeof(ResetCommandLineOptions),
        typeof(GenerateTpnCommandLineOptions),
        typeof(CloakedFileScanOptions),
        typeof(GetRepoVersionCommandLineOptions),
        typeof(VmrPushCommandLineOptions),
        typeof(VmrDiffOptions),
        typeof(MergeBandsCommandLineOptions),
    ];
}
