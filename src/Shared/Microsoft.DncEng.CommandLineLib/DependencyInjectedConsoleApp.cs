using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.ApplicationInsights.Channel;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.DncEng.CommandLineLib.Authentication;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Mono.Options;

namespace Microsoft.DncEng.CommandLineLib;

public abstract class DependencyInjectedConsoleApp
{
    protected abstract void ConfigureServices(IServiceCollection services);

    protected virtual void RegisterConfiguration(IServiceCollection services)
    {
        string settingsFile = Path.Join(AppContext.BaseDirectory, "appsettings.json");
        IConfigurationRoot config = new ConfigurationBuilder()
            .AddJsonFile(settingsFile, optional: true, reloadOnChange: false)
            .Build();

        services.AddSingleton(config);
        services.AddSingleton((IConfiguration)config);
    }

    private void ConfigureDefaultServices(IServiceCollection services)
    {
        services.TryAddSingleton<ICommandOptions, CommandOptions>();
        services.TryAddSingleton<ICommandRegistry, DefaultCommandRegistry>();
        services.TryAddSingleton<IConsoleBackend, DefaultConsoleBackend>();
        services.TryAddSingleton<IConsole, DefaultConsole>();
        services.TryAddSingleton<ISystemClock, SystemClock>();
        services.TryAddSingleton<InteractiveTokenCredentialProvider>();
        services.TryAddSingleton<TokenCredentialProvider>();
        services.AddLogging(ConfigureLogging);

        services.TryAddSingleton<ITelemetryInitializer>(provider => provider.GetRequiredService<InteractiveTokenCredentialProvider>());
        services.AddSingleton<ITelemetryInitializer, BasicInitializer>();
        services.AddSingleton<IConfigureOptions<TelemetryConfiguration>>(
            provider => new ConfigureNamedOptions<TelemetryConfiguration>(
                Options.DefaultName,
                c =>
                {
                    if (Debugger.IsAttached)
                    {
                        c.TelemetryChannel.DeveloperMode = true;
                    }
                    else
                    {
                        c.InstrumentationKey = provider.GetRequiredService<IConfiguration>()["ApplicationInsightsInstrumentationKey"] ?? "";
                    }
                }
            )
        );

    }

    private class BasicInitializer : ITelemetryInitializer
    {
        public void Initialize(ITelemetry telemetry)
        {
            telemetry.Context.Operation.Id = Activity.Current?.Id;
            telemetry.Context.Operation.Name = Activity.Current?.OperationName;
        }
    }


    protected virtual void ConfigureLogging(ILoggingBuilder builder)
    {
        builder.AddApplicationInsights(options =>
        {
            options.FlushOnDispose = true;
            options.IncludeScopes = false;
            options.TrackExceptionsAsExceptionTelemetry = true;
        });
    }

    public async Task<int> RunAsync(IEnumerable<string> args)
    {
        var services = new ServiceCollection();

        RegisterConfiguration(services);
        ConfigureServiceCollection(services);

        await using ServiceProvider provider = services.BuildServiceProvider();
        var activity = new Activity("Root").SetIdFormat(ActivityIdFormat.Hierarchical).Start();
        try
        {
            await RunCommandAsync(provider, args);
            return ExitCodes.Success;
        }
        catch (FailWithExitCodeException e)
        {
            if (e.ShowMessage)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                await Console.Error.WriteLineAsync(e.Message);
                Console.ResetColor();
            }

            return e.ExitCode;
        }
        finally
        {
            activity.Stop();
        }
    }

    public void ConfigureServiceCollection(ServiceCollection services)
    {
        ConfigureDefaultServices(services);
        ConfigureServices(services);
    }


    public static async Task RunCommandAsync(
        IServiceProvider provider,
        IEnumerable<string> args)
    {
        Command currentCommand;

        void ShowHelp(List<OptionSet> options, IReadOnlyDictionary<string, Type> commands)
        {
            var console = provider.GetRequiredService<IConsoleBackend>();
            console.Out.WriteLine(currentCommand.GetDetailedHelpText());
            console.Out.WriteLine();
            WriteGeneralUsage(console.Out, options, commands);
        }

        var commandTree = new List<Command>();
        var allOptions = new List<OptionSet>();
        var globalCommand = ActivatorUtilities.CreateInstance<GlobalCommand>(provider);
        var commandNames = new List<string>();
        currentCommand = globalCommand;

        var logger = provider.GetRequiredService<ILogger<DependencyInjectedConsoleApp>>();

        while (true)
        {
            // There are some unused arguments, and we have a child command to execute
            var commandOptions = provider.GetService<ICommandOptions>() as CommandOptions;
            commandOptions?.RegisterOptions(currentCommand);
            commandTree.Add(currentCommand);
            IReadOnlyDictionary<string, Type> commands = provider.GetRequiredService<ICommandRegistry>()
                .GetValidCommandAtScope(currentCommand.GetType());
            OptionSet optionSet = currentCommand.GetOptions();
            allOptions.Add(optionSet);
            List<string> unused = optionSet.Parse(args);
            unused = currentCommand.HandlePositionalArguments(unused);

            if (unused.Count > 0 && string.Equals(unused[0], "help", StringComparison.OrdinalIgnoreCase))
            {
                // Help was requested!
                ShowHelp(allOptions, commands);
                return;
            }

            if (unused.Count == 0)
            {
                if (globalCommand.Help)
                {
                    // Help was requested!
                    ShowHelp(allOptions, commands);
                    return;
                }

                // All arguments used, success!

                foreach (Command c in commandTree)
                {
                    if (!c.AreRequiredOptionsSet())
                    {
                        var console = provider.GetRequiredService<IConsoleBackend>();
                        console.SetColor(ConsoleColor.Red);
                        await console.Error.WriteLineAsync("Required parameters not set");
                        console.ResetColor();
                        WriteGeneralUsage(console.Error, allOptions, ImmutableDictionary<string, Type>.Empty);
                        throw new FailWithExitCodeException(ExitCodes.RequiredParameter);
                    }
                }

                Activity activity = new Activity($"EXEC {string.Join(" ", commandNames)}").SetIdFormat(ActivityIdFormat.Hierarchical).Start();
                try
                {
                    // Set up cancellation with CTRL-C, so we can attempt to "cancel" cleanly
                    var ctrlC = new CancellationTokenSource();
                    Console.CancelKeyPress += (sender, eventArgs) =>
                    {
                        if (ctrlC.IsCancellationRequested)
                        {
                            // We are already cancelling, they double cancelled!
                            logger.LogError("Force cancellation requested");
                            Environment.Exit(ExitCodes.Break);
                            return;
                        }

                        ctrlC.Cancel();
                        Console.Error.WriteLine(
                            "Ctrl-C pressed, cancelling operation... (Press again to force cancellation)"
                        );
                        logger.LogWarning("Cancellation requested");
                        eventArgs.Cancel = true;
                    };

                    Task runTask = currentCommand.RunAsync(ctrlC.Token);
                    if (runTask == null)
                    {
                        // No implementation, we need a subcommand, dump usage
                        var console = provider.GetRequiredService<IConsoleBackend>();
                        WriteGeneralUsage(console.Error, allOptions, commands);
                        throw new FailWithExitCodeException(ExitCodes.MissingCommand);
                    }

                    try
                    {
                        await runTask;
                        return;
                    }
                    catch (FailWithExitCodeException)
                    {
                        throw;
                    }
                    catch (Exception e)
                    {
                        logger.LogError(e, "Unhandled exception");
                        var console = provider.GetRequiredService<IConsole>();
                        console.WriteError("Unhandled exception: " + e.Message);
                        throw new FailWithExitCodeException(ExitCodes.UnhandledException);
                    }
                }
                finally
                {
                    activity.Stop();
                }
            }

            var commandToExecute = unused[0];
            unused.RemoveAt(0);

            if (string.Equals(commandToExecute, "help", StringComparison.OrdinalIgnoreCase))
            {
                provider.GetService<ICommandOptions>().GetOptions<GlobalCommand>().Help = true;
            }

            if (!commands.TryGetValue(commandToExecute, out Type childCommandType))
            {
                if (provider.GetService<ICommandOptions>().GetOptions<GlobalCommand>().Help)
                {
                    // We are out of commands, and help was requested, so let's do the most specific help possible
                    ShowHelp(allOptions, commands);
                    return;
                }

                // No help, and we didn't understand what they asked... error!
                var console = provider.GetRequiredService<IConsoleBackend>();
                console.SetColor(ConsoleColor.Red);
                await console.Error.WriteLineAsync($"Unrecognized argument/command '{commandToExecute}'");
                console.ResetColor();
                WriteGeneralUsage(console.Error, allOptions, commands);
                throw new FailWithExitCodeException(ExitCodes.UnknownArgument);
            }

            commandNames.Add(commandToExecute);
            args = unused;
            currentCommand = (Command)ActivatorUtilities.CreateInstance(provider, childCommandType);
        }
    }

    private static void WriteGeneralUsage(
        TextWriter writer,
        IEnumerable<OptionSet> optionSet,
        IReadOnlyDictionary<string, Type> commands)
    {
        writer.WriteLine("Options:");
        foreach (OptionSet o in optionSet)
        {
            o.WriteOptionDescriptions(writer);
        }

        if (commands.Count > 0)
        {
            writer.WriteLine();
            writer.WriteLine("Commands");
            foreach ((string validCommand, Type commandType) in commands)
            {
                string description = commandType.GetCustomAttribute<CommandAttribute>()?.Description ??
                                     "<<no description>>";
                writer.WriteLine($"  {validCommand} - {description}");
            }
        }
    }

}
