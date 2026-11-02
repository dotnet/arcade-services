// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.CommandLine;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.DotNet.Darc.Operations;
using Microsoft.DotNet.Darc.Options;
using Microsoft.DotNet.Darc.Options.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Microsoft.DotNet.Darc;

internal static class Program
{
    private const string DarcCommandName = "darc";
    private const string RootCommandDescription = "Darc is the tool for managing the .NET dependency flow.";
    private const string VmrCommandName = "vmr";
    private const string VmrCommandDescription = "Commands for managing the Virtual Monolithic Repository (VMR).";

    private static async Task<int> Main(string[] args)
    {
        if (args.Contains("--debug"))
        {
            // Print header (version, sha, arguments)
            var version = FileVersionInfo.GetVersionInfo(Assembly.GetExecutingAssembly().Location);
            Console.WriteLine(
                $"[{version.ProductVersion} / {version.OriginalFilename}] " +
                "darc command issued: " + string.Join(' ', args));
        }

        // Help and parse errors have always gone to stderr so that piping darc output stays usable
        InvocationConfiguration configuration = new()
        {
            Output = Console.Error,
            Error = Console.Error,
            ProcessTerminationTimeout = null,
        };

        return await Parse(BuildCommandLine(), args).InvokeAsync(configuration);
    }

    /// <summary>
    /// Parses the given command line the same way darc does at runtime.
    /// </summary>
    /// <remarks>
    /// Response file expansion is disabled because darc option values are allowed to start with '@'
    /// (e.g. --failure-notification-tags @dotnet/some-team) and have never been read from a file.
    /// </remarks>
    internal static ParseResult Parse(Command command, params string[] args)
        => command.Parse(CliOptions.AttachShortOptionValues(command, args), new ParserConfiguration { ResponseFileTokenReplacer = null });

    private static RootCommand BuildCommandLine()
    {
        // The assembly is named Microsoft.DotNet.Darc but the tool is invoked as `darc`
        RootCommand root = new(RootCommandDescription)
        {
            HelpName = DarcCommandName,
        };

        foreach (CommandLineOptions options in GetOptions())
        {
            root.Add(CreateCommand(options));
        }

        Command vmr = new(VmrCommandName, VmrCommandDescription);
        foreach (CommandLineOptions options in GetVmrOptions())
        {
            vmr.Add(CreateCommand(options));
        }

        root.Add(vmr);

        return root;
    }

    internal static Command CreateCommand(CommandLineOptions options)
    {
        Command command = new(options.Verb, options.VerbDescription);

        foreach (CliSymbol symbol in options.Symbols)
        {
            switch (symbol.Symbol)
            {
                case Argument argument:
                    command.Add(argument);
                    break;
                case Option option:
                    command.Add(option);
                    break;
            }
        }

        foreach (var validator in options.Validators)
        {
            command.Validators.Add(validator);
        }

        command.SetAction((parseResult, _) => ExecuteAsync(options, parseResult));

        return command;
    }

    private static async Task<int> ExecuteAsync(CommandLineOptions options, ParseResult parseResult)
    {
        try
        {
            options.Bind(parseResult);
        }
        catch (Exception e)
        {
            // Some properties validate in their setter (e.g. --output-format), which darc has always
            // reported as a command line error rather than as a crash. Report it with the same exit
            // code as any other invalid input instead of the operation failure code.
            Console.Error.WriteLine("ERROR(S):");
            Console.Error.WriteLine("  " + e.Message);
            return Constants.CommandLineErrorCode;
        }

        ServiceCollection services = new();
        options.RegisterServices(services);

        using ServiceProvider provider = services.BuildServiceProvider();
        options.InitializeFromSettings(provider.GetRequiredService<ILogger>());

        if (!options.IsCi && !await DarcVersionValidator.ValidateAsync(
                options.BuildAssetRegistryBaseUri,
                provider.GetRequiredService<ILogger>()))
        {
            return Constants.ErrorCode;
        }

        var ret = await RunOperationAsync(options, provider);

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
    }

    /// <summary>
    /// Runs the operation and calls dispose afterwards, returning the operation exit code.
    /// </summary>
    /// <param name="opts">Options of the operation to run</param>
    /// <returns>Exit code of the operation</returns>
    /// <remarks>The primary reason for this is a workaround for an issue in the logging factory which
    /// causes it to not dispose the logging providers on process exit.  This causes missed logs, logs that end midway through
    /// and cause issues with the console coloring, etc.</remarks>
    private static async Task<int> RunOperationAsync(CommandLineOptions opts, ServiceProvider sp)
    {
        try
        {
            Operation operation = opts.GetOperation(sp);

            return await operation.ExecuteAsync();
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
    public static CommandLineOptions[] GetOptions() =>
    [
        new AddChannelCommandLineOptions(),
        new AddDependencyCommandLineOptions(),
        new AddDefaultChannelCommandLineOptions(),
        new AddSubscriptionCommandLineOptions(),
        new AddBuildToChannelCommandLineOptions(),
        new AuthenticateCommandLineOptions(),
        new LoginCommandLineOptions(),
        new DefaultChannelStatusCommandLineOptions(),
        new DeleteBuildFromChannelCommandLineOptions(),
        new DeleteChannelCommandLineOptions(),
        new DeleteDefaultChannelCommandLineOptions(),
        new DeleteSubscriptionsCommandLineOptions(),
        new GatherDropCommandLineOptions(),
        new GetAssetCommandLineOptions(),
        new GetBuildCommandLineOptions(),
        new GetChannelCommandLineOptions(),
        new GetChannelsCommandLineOptions(),
        new GetDefaultChannelsCommandLineOptions(),
        new GetDependenciesCommandLineOptions(),
        new GetDependencyGraphCommandLineOptions(),
        new GetDependencyFlowGraphCommandLineOptions(),
        new GetHealthCommandLineOptions(),
        new GetLatestBuildCommandLineOptions(),
        new GetRepositoryMergePoliciesCommandLineOptions(),
        new GetSubscriptionsCommandLineOptions(),
        new GetSubscriptionHistoryCommandLineOptions(),
        new SetRepositoryMergePoliciesCommandLineOptions(),
        new SubscriptionsStatusCommandLineOptions(),
        new TriggerSubscriptionsCommandLineOptions(),
        new UpdateBuildCommandLineOptions(),
        new UpdateChannelCommandLineOptions(),
        new UpdateDependenciesCommandLineOptions(),
        new UpdateSubscriptionCommandLineOptions(),
        new VerifyCommandLineOptions(),
        new SetGoalCommandLineOptions(),
        new GetGoalCommandLineOptions(),
    ];

    // These are under the "vmr" subcommand
    public static CommandLineOptions[] GetVmrOptions() =>
    [
        new AddRepoCommandLineOptions(),
        new RemoveRepoCommandLineOptions(),
        new BackflowCommandLineOptions(),
        new ForwardFlowCommandLineOptions(),
        new ResolveConflictCommandLineOptions(),
        new CherryPickCommandLineOptions(),
        new ResetCommandLineOptions(),
        new ResetSubmoduleCommandLineOptions(),
        new GenerateTpnCommandLineOptions(),
        new CloakedFileScanOptions(),
        new GetRepoVersionCommandLineOptions(),
        new VmrPushCommandLineOptions(),
        new VmrDiffOptions(),
        new MergeBandsCommandLineOptions(),
    ];
}
