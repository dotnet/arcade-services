// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.DotNet.Darc.Options;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.HealthMetrics;
using Microsoft.DotNet.DarcLib.Models.Darc;
using Microsoft.DotNet.Maestro.Client;
using Microsoft.DotNet.Maestro.Client.Models;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.Services.Common;

namespace Microsoft.DotNet.Darc.Operations;

/// <summary>
///     Represents a tuple of a metric and the associated formatted output that
///     darc should display after running the metric
/// </summary>
internal class HealthMetricWithOutput
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
    private readonly GetHealthCommandLineOptions _options;
    private readonly IRemoteFactory _remoteFactory;
    private readonly IBarApiClient _barClient;
    private readonly ILogger<GetHealthOperation> _logger;

    public GetHealthOperation(
        GetHealthCommandLineOptions options,
        IBarApiClient barClient,
        IRemoteFactory remoteFactory,
        ILogger<GetHealthOperation> logger)
    {
        _options = options;
        _barClient = barClient;
        _remoteFactory = remoteFactory;
        _logger = logger;
    }

    public override async Task<int> ExecuteAsync()
    {
        try
        {
            IEnumerable<Subscription> subscriptions = await _barClient.GetSubscriptionsAsync();
            IEnumerable<DefaultChannel> defaultChannels = await _barClient.GetDefaultChannelsAsync();
            IEnumerable<Channel> channels = await _barClient.GetChannelsAsync();

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

            List<Func<Task<HealthMetricWithOutput>>> metricsToRun = ComputeMetricsToRun(
                channelsToEvaluate,
                reposToEvaluate,
                subscriptions,
                defaultChannels);

            // Run the metrics
            HealthMetricWithOutput[] results = await Task.WhenAll(metricsToRun.Select(metric => metric()));

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
        catch (AuthenticationException e)
        {
            Console.WriteLine(e.Message);
            return Constants.ErrorCode;
        }
    }

    /// <summary>
    ///     Create a list of metrics to run.
    /// </summary>
    /// <param name="channelsToEvaluate">Channels to evaluate</param>
    /// <param name="reposToEvaluate">Repositories to evaluate</param>
    /// <returns>List of Func's that, when evaluated, will produce metrics with output.</returns>
    private List<Func<Task<HealthMetricWithOutput>>> ComputeMetricsToRun(
        HashSet<string> channelsToEvaluate,
        HashSet<string> reposToEvaluate,
        IEnumerable<Subscription> subscriptions,
        IEnumerable<DefaultChannel> defaultChannels) =>
    [
        .. ComputeSubscriptionHealthMetricsToRun(channelsToEvaluate, reposToEvaluate, subscriptions, defaultChannels),
        .. ComputeProductDependencyCycleMetricsToRun(channelsToEvaluate, reposToEvaluate, subscriptions, defaultChannels),
    ];

    /// <summary>
    ///     Get typical repository and branch combinations for use with repo+branch focused metrics.
    /// </summary>
    /// <param name="channelsToEvaluate">Channels that should be evaluated.</param>
    /// <param name="reposToEvaluate">Repositories that should be evaluated.</param>
    /// <param name="subscriptions">All subscriptions.</param>
    /// <param name="defaultChannels">All default channel associations.</param>
    /// <returns>Set of repo+branch combinations that should be evaluated.</returns>
    private static HashSet<(string repo, string branch)> GetRepositoryBranchCombinations(HashSet<string> channelsToEvaluate,
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
    private List<Func<Task<HealthMetricWithOutput>>> ComputeSubscriptionHealthMetricsToRun(
        HashSet<string> channelsToEvaluate,
        HashSet<string> reposToEvaluate,
        IEnumerable<Subscription> subscriptions,
        IEnumerable<DefaultChannel> defaultChannels)
    {

        HashSet<(string repo, string branch)> repoBranchCombinations =
            GetRepositoryBranchCombinations(channelsToEvaluate, reposToEvaluate, subscriptions, defaultChannels);

        return repoBranchCombinations.Select<(string repo, string branch), Func<Task<HealthMetricWithOutput>>>(t =>
                async () =>
                {
                    var healthMetric = new SubscriptionHealthMetric(
                        t.repo,
                        t.branch,
                        d => true,
                        _remoteFactory,
                        _barClient,
                        _logger);

                    await healthMetric.EvaluateAsync();

                    var outputBuilder = new StringBuilder();

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
    private List<Func<Task<HealthMetricWithOutput>>> ComputeProductDependencyCycleMetricsToRun(
        HashSet<string> channelsToEvaluate,
        HashSet<string> reposToEvaluate,
        IEnumerable<Subscription> subscriptions,
        IEnumerable<DefaultChannel> defaultChannels)
    {
        HashSet<(string repo, string branch)> repoBranchCombinations =
            GetRepositoryBranchCombinations(channelsToEvaluate, reposToEvaluate, subscriptions, defaultChannels);

        return repoBranchCombinations.Select<(string repo, string branch), Func<Task<HealthMetricWithOutput>>>(t =>
                async () =>
                {
                    var healthMetric = new ProductDependencyCyclesHealthMetric(t.repo, t.branch, _remoteFactory, _barClient, _logger);

                    await healthMetric.EvaluateAsync();

                    var outputBuilder = new StringBuilder();

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

    private HashSet<string> ComputeChannelsToEvaluate(IEnumerable<Channel> channels)
    {
        if (!string.IsNullOrEmpty(_options.Channel))
        {
            var channelsToTarget = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
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
        var defaultChannelRepositories = defaultChannels
            .Where(df => string.IsNullOrEmpty(_options.Repo) || df.Repository.Contains(_options.Repo, StringComparison.OrdinalIgnoreCase))
            .Select(df => df.Repository);

        var targetRepositories = subscriptions
            .Where(s => string.IsNullOrEmpty(_options.Repo) || s.TargetRepository.Contains(_options.Repo, StringComparison.OrdinalIgnoreCase))
            .Select(s => s.TargetRepository);

        var sourceRepositories = subscriptions
            .Where(s => string.IsNullOrEmpty(_options.Repo) || s.SourceRepository.Contains(_options.Repo, StringComparison.OrdinalIgnoreCase))
            .Select(s => s.SourceRepository);

        return defaultChannelRepositories
            .Concat(sourceRepositories)
            .Concat(sourceRepositories)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }
}
