// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.DotNet.Darc.Helpers;
using Microsoft.DotNet.Darc.Options;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.HealthMetrics;
using Microsoft.DotNet.Maestro.Client.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Microsoft.DotNet.Darc.Operations
{
    /// <summary>
    ///     Represents a tuple of a metric and the associated formatted output that
    ///     darc should display after running the metric
    /// </summary>
    class HealthMetricWithOutput
    {
        public HealthMetricWithOutput(HealthMetric metric, string formattedOutput)
        {
            Metric = metric;
            FormattedConsoleOutput = formattedOutput;
        }

        /// <summary>
        ///     Metric (includes raw results)
        /// </summary>
        public readonly HealthMetric Metric;
        /// <summary>
        ///     Formatted console output specifically for darc display.
        /// </summary>
        public readonly string FormattedConsoleOutput;
    }

    /// <summary>
    ///     Implements a 'get-health' operation, a generalized way to look at PKPIs (product key performance
    ///     metrics) across the stack.
    /// </summary>
    internal class GetHealthOperation : Operation
    {
        GetHealthCommandLineOptions _options;
        public GetHealthOperation(GetHealthCommandLineOptions options)
            : base(options)
        {
            _options = options;
        }

        public override async Task<int> ExecuteAsync()
        {
            IRemote remote = RemoteFactory.GetBarOnlyRemote(_options, Logger);

            IEnumerable<Subscription> subscriptions = await remote.GetSubscriptionsAsync();
            IEnumerable<DefaultChannel> defaultChannels = await remote.GetDefaultChannelsAsync();
            IEnumerable<Channel> channels = await remote.GetChannelsAsync();

            HashSet<string> channelsToEvaluate = ComputeChannelsToEvaluate(channels);
            HashSet<string> reposToEvaluate = ComputeRepositoriesToEvaluate(defaultChannels, subscriptions);

            // Print out what will be evaluated. If no channels or repos are in the initial sets, then
            // this is currently an error. Because different PKPIs apply to different input items differently,
            // this check may not be useful in the future.

            if (channelsToEvaluate.Any())
            {
                Console.WriteLine("Evaluating the following channels:");
                foreach (string channel in channelsToEvaluate)
                {
                    Console.WriteLine($"  {channel}");
                }
            }
            else
            {
                Console.WriteLine($"There were no channels found to evaluate based on inputs, exiting.");
                return Constants.ErrorCode;
            }

            if (reposToEvaluate.Any())
            {
                Console.WriteLine("Evaluating the following repositories:");
                foreach (string repo in reposToEvaluate)
                {
                    Console.WriteLine($"  {repo}");
                }
            }
            else
            {
                Console.WriteLine($"There were no repositories found to evaluate based on inputs, exiting.");
                return Constants.ErrorCode;
            }

            Console.WriteLine();

            // Compute metrics, then run in parallel.

            List<Func<Task<HealthMetricWithOutput>>> metricsToRun = ComputeMetricsToRun(channelsToEvaluate, reposToEvaluate,
                subscriptions, defaultChannels, channels);

            // Run the metrics
            HealthMetricWithOutput[] results = await Task.WhenAll<HealthMetricWithOutput>(metricsToRun.Select(metric => metric()));

            // Walk through and print the results out
            bool passed = true;
            foreach (var healthResult in results)
            {
                if (healthResult.Metric.Result != HealthResult.Passed)
                {
                    passed = false;
                }

                Console.WriteLine($"{healthResult.Metric.MetricDescription} - ({healthResult.Metric.Result})");
                if (healthResult.Metric.Result != HealthResult.Passed)
                {
                    Console.WriteLine();
                    Console.WriteLine(healthResult.FormattedConsoleOutput);
                }
            }

            return passed ? Constants.SuccessCode : Constants.ErrorCode;
        }

        /// <summary>
        ///     Create a list of metrics to run.
        /// </summary>
        /// <param name="channelsToEvaluate">Channels to evaluate</param>
        /// <param name="reposToEvaluate">Repositories to evaluate</param>
        /// <returns>List of Func's that, when evaluated, will produce metrics with output.</returns>
        private List<Func<Task<HealthMetricWithOutput>>> ComputeMetricsToRun(HashSet<string> channelsToEvaluate,
            HashSet<string> reposToEvaluate, IEnumerable<Subscription> subscriptions,
            IEnumerable<DefaultChannel> defaultChannels, IEnumerable<Channel> channels)
        {
            var metricsToRun = new List<Func<Task<HealthMetricWithOutput>>>();

            metricsToRun.AddRange(ComputeSubscriptionHealthMetricsToRun(channelsToEvaluate, reposToEvaluate, subscriptions, defaultChannels));
            metricsToRun.AddRange(ComputeProductDependencyCycleMetricsToRun(channelsToEvaluate, reposToEvaluate, subscriptions, defaultChannels));

            return metricsToRun;
        }

        /// <summary>
        ///     Get typical repository and branch combinations for use with repo+branch focused metrics.
        /// </summary>
        /// <param name="channelsToEvaluate">Channels that should be evaluated.</param>
        /// <param name="reposToEvaluate">Repositories that should be evaluated.</param>
        /// <param name="subscriptions">All subscriptions.</param>
        /// <param name="defaultChannels">All default channel associations.</param>
        /// <returns>Set of repo+branch combinations that should be evaluated.</returns>
        private HashSet<(string repo, string branch)> GetRepositoryBranchCombinations(HashSet<string> channelsToEvaluate,
            HashSet<string> reposToEvaluate, IEnumerable<Subscription> subscriptions, IEnumerable<DefaultChannel> defaultChannels)
        {
            // Compute the combinations that make sense.
            IEnumerable<(string repo, string branch)> defaultChannelsRepoBranchCombinations = defaultChannels
                .Where(df => channelsToEvaluate.Contains(df.Channel.Name))
                .Where(df => reposToEvaluate.Contains(df.Repository))
                .Select<DefaultChannel, (string repo, string branch)>(df => (df.Repository, df.Branch));

            HashSet<(string repo, string branch)> repoBranchCombinations = subscriptions
                .Where(s => reposToEvaluate.Contains(s.TargetRepository))
                .Where(s => channelsToEvaluate.Contains(s.Channel.Name))
                .Select<Subscription, (string repo, string branch)>(s => (s.TargetRepository, s.TargetBranch))
                .ToHashSet();

            repoBranchCombinations.UnionWith(defaultChannelsRepoBranchCombinations);

            return repoBranchCombinations;
        }

        /// <summary>
        ///     Compute the subscription health metrics to run based on the channels and input repositories
        /// </summary>
        /// <param name="channelsToEvaluate">Channels in the initial set.</param>
        /// <param name="reposToEvaluate">Repositories in the initial set.</param>
        /// <param name="subscriptions">Subscriptions in the build asset registry.</param>
        /// <returns>List of subscription health metrics</returns>
        /// <remarks>
        ///     Subscription health is based on repo and branch.  We need to find all the combinations that
        ///     that make sense to evaluate.
        ///     
        ///     Since this is a target-of-subscription focused metric, use the set of target repos+branches from
        ///     <paramref name="subscriptions"/> where the target repo is in <paramref name="reposToEvaluate"/> and
        ///     the source channel is in <paramref name="channelsToEvaluate"/> or the target branch is in branches.
        ///     Also add in repos (in <paramref name="reposToEvaluate"/>) who have a default channel association targeting
        ///     a channel in <paramref name="channelsToEvaluate"/>
        ///     
        ///     Note that this will currently miss completely untargeted branches, until those have at least one
        ///     default channel or subscription. This is a fairly benign limitation.
        /// </remarks>
        private List<Func<Task<HealthMetricWithOutput>>> ComputeSubscriptionHealthMetricsToRun(HashSet<string> channelsToEvaluate,
            HashSet<string> reposToEvaluate, IEnumerable<Subscription> subscriptions, IEnumerable<DefaultChannel> defaultChannels)
        {
            IRemoteFactory remoteFactory = new RemoteFactory(_options);

            HashSet<(string repo, string branch)> repoBranchCombinations =
                GetRepositoryBranchCombinations(channelsToEvaluate, reposToEvaluate, subscriptions, defaultChannels);

            return repoBranchCombinations.Select<(string repo, string branch), Func<Task<HealthMetricWithOutput>>>(t =>
                async () =>
                {
                    SubscriptionHealthMetric healthMetric = new SubscriptionHealthMetric(t.repo, t.branch, 
                        d => true, Logger, remoteFactory);

                    await healthMetric.EvaluateAsync();

                    StringBuilder outputBuilder = new StringBuilder();

                    if (healthMetric.ConflictingSubscriptions.Any())
                    {
                        outputBuilder.AppendLine($"  Conflicting subscriptions:");
                        foreach (var conflict in healthMetric.ConflictingSubscriptions)
                        {
                            outputBuilder.AppendLine($"    {conflict.Asset} would be updated by the following subscriptions:");
                            foreach (var subscription in conflict.Subscriptions)
                            {
                                outputBuilder.AppendLine($"      {UxHelpers.GetSubscriptionDescription(subscription)} ({subscription.Id})");
                            }
                        }
                    }

                    if (healthMetric.DependenciesMissingSubscriptions.Any())
                    {
                        outputBuilder.AppendLine($"  Dependencies missing subscriptions:");
                        foreach (DependencyDetail dependency in healthMetric.DependenciesMissingSubscriptions)
                        {
                            outputBuilder.AppendLine($"    {dependency.Name}");
                        }
                    }

                    if (healthMetric.DependenciesThatDoNotFlow.Any())
                    {
                        outputBuilder.AppendLine($"  Dependencies that do not flow automatically (disabled or frequency=none):");
                        foreach (DependencyDetail dependency in healthMetric.DependenciesThatDoNotFlow)
                        {
                            outputBuilder.AppendLine($"    {dependency.Name}");
                        }
                    }

                    if (healthMetric.UnusedSubscriptions.Any())
                    {
                        outputBuilder.AppendLine($"  Subscriptions that do not have any effect:");
                        foreach (Subscription subscription in healthMetric.UnusedSubscriptions)
                        {
                            outputBuilder.AppendLine($"    {UxHelpers.GetSubscriptionDescription(subscription)} ({subscription.Id})");
                        }
                    }

                    return new HealthMetricWithOutput(healthMetric, outputBuilder.ToString());
                })
            .ToList();
        }

        /// <summary>
        ///     Compute product dependency cycle metrics based on the input repositories and channels.
        /// </summary>
        /// <param name="channelsToEvaluate"></param>
        /// <returns></returns>
        private List<Func<Task<HealthMetricWithOutput>>> ComputeProductDependencyCycleMetricsToRun(HashSet<string> channelsToEvaluate,
            HashSet<string> reposToEvaluate, IEnumerable<Subscription> subscriptions, IEnumerable<DefaultChannel> defaultChannels)
        {
            IRemoteFactory remoteFactory = new RemoteFactory(_options);

            HashSet<(string repo, string branch)> repoBranchCombinations =
                GetRepositoryBranchCombinations(channelsToEvaluate, reposToEvaluate, subscriptions, defaultChannels);

            return repoBranchCombinations.Select<(string repo, string branch), Func<Task<HealthMetricWithOutput>>>(t =>
                async () =>
                {
                    ProductDependencyCyclesHealthMetric healthMetric = new ProductDependencyCyclesHealthMetric(t.repo, t.branch,
                        Logger, remoteFactory);

                    await healthMetric.EvaluateAsync();

                    StringBuilder outputBuilder = new StringBuilder();

                    if (healthMetric.Cycles.Any())
                    {
                        outputBuilder.AppendLine($"  Product Dependency Cycles:");
                        foreach (var cycle in healthMetric.Cycles)
                        {
                            outputBuilder.AppendLine($"    {string.Join(" -> ", cycle)} -> ...");
                        }
                    }

                    return new HealthMetricWithOutput(healthMetric, outputBuilder.ToString());
                })
            .ToList();
        }

            /// <summary>
            /// 
            /// </summary>
            /// <param name="channels"></param>
            /// <returns></returns>
            private HashSet<string> ComputeChannelsToEvaluate(IEnumerable<Channel> channels)
        {
            if (!string.IsNullOrEmpty(_options.Channel))
            {
                HashSet<string> channelsToTarget = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                Channel targetChannel = UxHelpers.ResolveSingleChannel(channels, _options.Channel);

                if (targetChannel != null)
                {
                    channelsToTarget.Add(targetChannel.Name);
                }

                return channelsToTarget;
            }
            else
            {
                // Look up all channels
                return channels.Select(c => c.Name).ToHashSet();
            }
        }

        /// <summary>
        ///     Compute maximal initial set of input repositories
        /// </summary>
        /// <param name="remote">BAR remote</param>
        /// <returns>Repositories to evaluate</returns>
        private HashSet<string> ComputeRepositoriesToEvaluate(IEnumerable<DefaultChannel> defaultChannels, IEnumerable<Subscription> subscriptions)
        {
            // Compute which repositories to target
            HashSet<string> reposToTarget = defaultChannels
                .Where(df => string.IsNullOrEmpty(_options.Repo) || df.Repository.Contains(_options.Repo, StringComparison.OrdinalIgnoreCase))
                .Select(df => df.Repository)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            subscriptions
                .Where(s => string.IsNullOrEmpty(_options.Repo) || s.TargetRepository.Contains(_options.Repo, StringComparison.OrdinalIgnoreCase))
                .Select(s => reposToTarget.Add(s.TargetRepository));

            subscriptions
                .Where(s => string.IsNullOrEmpty(_options.Repo) || s.SourceRepository.Contains(_options.Repo, StringComparison.OrdinalIgnoreCase))
                .Select(s => reposToTarget.Add(s.SourceRepository));

            return reposToTarget;
        }
    }
}
