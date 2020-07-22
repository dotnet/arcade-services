// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using CommandLine;
using Microsoft.DotNet.Darc.Operations;
using Microsoft.DotNet.Darc.Options;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Channel;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.DependencyCollector;
using Microsoft.ApplicationInsights.Extensibility;

namespace Microsoft.DotNet.Darc
{
    internal static class Program
    {
        private static TelemetryClient s_telemetryClient;

        private static int Main(string[] args)
        {
            InitializeTelemetry();

            try
            {
                return Parser.Default.ParseArguments(args, GetOptions())
                    .MapResult(
                        (CommandLineOptions opts) => RunOperation(opts),
                        RecordFailedCommand
                    );
            }
            finally
            {
                s_telemetryClient.Flush();
            }
        }

        private static int RecordFailedCommand(IEnumerable<Error> errors)
        {
            foreach (Error error in errors)
            {
                switch (error)
                {
                    case NamedError named:
                        s_telemetryClient.TrackTrace(
                            $"Command line parsing error: {named.Tag}, name '{named.NameInfo.LongName}'",
                            SeverityLevel.Error,
                            new Dictionary<string, string>
                            {
                                {"errorTag", named.Tag.ToString()},
                                {"errorName", named.NameInfo.LongName},
                            }
                        );
                        break;
                    case TokenError token:
                        s_telemetryClient.TrackTrace(
                            $"Command line parsing error: {token.Tag}, token '{token.Token}'",
                            SeverityLevel.Error,
                            new Dictionary<string, string>
                            {
                                {"errorTag", token.Tag.ToString()},
                                {"errorToken", token.Token},
                            }
                        );
                        break;
                    default:
                        s_telemetryClient.TrackTrace(
                            $"Command line parsing error: {error.Tag}",
                            SeverityLevel.Error,
                            new Dictionary<string, string>
                            {
                                {"errorTag", error.Tag.ToString()}
                            }
                        );
                        break;
                    case HelpRequestedError _:
                        ReportVerb("help");
                        return 0;
                    case HelpVerbRequestedError verbHelp:
                        ReportVerb("verb.help", "for", verbHelp.Verb);
                        return 0;
                    case VersionRequestedError _:
                        ReportVerb("version");
                        return 0;
                }
            }

            return 1;
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
                    Stopwatch stopwatch = Stopwatch.StartNew();
                    try
                    {
                        return operation.ExecuteAsync().GetAwaiter().GetResult();
                    }
                    finally
                    {
                        stopwatch.Stop();
                        ReportInvocation(opts, stopwatch.Elapsed);
                    }
                }
            }
            catch (Exception e)
            {
                Console.WriteLine("Unhandled exception encountered");
                Console.WriteLine(e);
                s_telemetryClient.TrackException(e);
                return Constants.ErrorCode;
            }
        }

        private static Type[] GetOptions()
        {
            // This order will mandate the order in which the commands are displayed if typing just 'darc'
            // so keep these sorted.
            return new Type[]
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
                    typeof(GetGoalCommandLineOptions)
                };
        }

        private static void InitializeTelemetry()
        {
            var isDebugging = Debugger.IsAttached;
            var config = TelemetryConfiguration.CreateDefault();
            if (!isDebugging)
            {
                config.InstrumentationKey = "9fcef3c7-f401-41c7-9e91-1f6029c8dcc3";
            }

            var dependencyTracking = new DependencyTrackingTelemetryModule();
            dependencyTracking.ExcludeComponentCorrelationHttpHeadersOnDomains.Add("core.windows.net");
            dependencyTracking.Initialize(config);

            config.TelemetryInitializers.Add(new HttpDependenciesParsingTelemetryInitializer());
            var channel = new InMemoryChannel {DeveloperMode = isDebugging};
            config.TelemetryChannel = channel;

            s_telemetryClient = new TelemetryClient(config);
        }

        private static void ReportVerb(string verb, params string [] extra)
        {
            var properties = new Dictionary<string, string> {{"verb", verb}};
            for (int i = 0; i < extra.Length - 1; i += 2)
            {
                properties.Add(extra[i], extra[i + 1]);
            }

            s_telemetryClient.TrackEvent("CommandExecuted",
                properties,
                new Dictionary<string, double>
                {
                    {"duration", 0}
                }
            );
        }

        private static void ReportInvocation(CommandLineOptions options, TimeSpan stopwatchElapsed)
        {
            Type optionType = options.GetType();
            string verb = optionType.GetCustomAttribute<VerbAttribute>()?.Name;
            if (string.IsNullOrEmpty(verb))
            {
                s_telemetryClient.TrackTrace($"Unrecognized options/verb detected: {optionType.Name}");
                return;
            }

            Dictionary<string,string> arguments = new Dictionary<string, string>();
            Dictionary<Type, object> defaultValueCache = new Dictionary<Type, object>();
            foreach (var prop in optionType.GetProperties(BindingFlags.FlattenHierarchy | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                var optionAttribute = prop.GetCustomAttribute<OptionAttribute>();
                if (optionAttribute == null)
                {
                    // Whatever this property is, it's not an option, ignore it
                    continue;
                }

                var value = prop.GetValue(options);
                if (value == null)
                {
                    // This argument wasn't passed, just ignore it
                    continue;
                }

                if (prop.PropertyType.IsValueType)
                {
                    // Value types, like "int", are hard, because they aren't null
                    // So we need to Activator.CreateInstance one to get the "default"
                    // value, and then compare to that.
                    if (!defaultValueCache.TryGetValue(prop.PropertyType, out var defaultValue))
                    {
                        defaultValueCache.Add(prop.PropertyType, defaultValue = Activator.CreateInstance(prop.PropertyType));
                    }

                    if (defaultValue.Equals(value))
                    {
                        continue;
                    }
                }

                if (prop.GetCustomAttribute<RedactFromLoggingAttribute>() != null)
                {
                    value = "<<REDACTED>>";
                }

                arguments.Add($"opt.{optionAttribute.LongName}", value.ToString());
            }

            arguments.Add("verb", verb);

            s_telemetryClient.TrackEvent("CommandExecuted",
                arguments,
                new Dictionary<string, double>
                {
                    {"duration", stopwatchElapsed.TotalMilliseconds}
                }
            );
        }
    }
}
