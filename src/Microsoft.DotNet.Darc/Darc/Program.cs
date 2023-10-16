// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using CommandLine;
using Microsoft.DotNet.Darc.Operations;
using Microsoft.DotNet.Darc.Options;
using Microsoft.DotNet.Darc.Options.VirtualMonoRepo;

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
                    (CommandLineOptions opts) => RunOperation(opts),
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
    private static int RunOperation(CommandLineOptions opts)
    {
        try
        {
            using (Operation operation = opts.GetOperation())
            {
                return operation.ExecuteAsync().GetAwaiter().GetResult();
            }
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
    private static Type[] GetOptions() => new[]
    {
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
        typeof(DeleteSubscriptionCommandLineOptions),
        typeof(DeleteSubscriptionsCommandLineOptions),
        typeof(GatherDropCommandLineOptions),
        typeof(GetAssetCommandLineOptions),
        typeof(GetBuildCommandLineOptions),
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
    };

    // These are under the "vmr" subcommand
    private static Type[] GetVmrOptions() => new[]
    {
        typeof(InitializeCommandLineOptions),
        typeof(UpdateCommandLineOptions),
        typeof(BackflowCommandLineOptions),
        typeof(GenerateTpnCommandLineOptions),
        typeof(CloakedFileScanOptions),
        typeof(BinaryFileScanOptions),
        typeof(GetRepoVersionCommandLineOptions),
        typeof(VmrPushCommandLineOptions)
    };
}
