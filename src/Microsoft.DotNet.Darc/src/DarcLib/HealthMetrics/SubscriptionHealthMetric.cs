using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.DotNet.Maestro.Client.Models;

namespace Microsoft.DotNet.DarcLib.HealthMetrics
{
    /// <summary>
    ///     Evaluate a branch in a repository for subscriptions that are missing or superfluous.
    ///     
    ///     Strictly speaking, we can't actually determine whether subscriptions are missing or superfluous.
    ///     Subscriptions are simply triggers to update a repository's dependencies based on the outputs produced from a build.
    ///     So whether or not subscriptions are missing is entirely dependent on what the *next* build will produce,
    ///     which is of course, unknowable.
    ///     
    ///     Fortunately, we can make a generally useful approximation by looking at the latest build
    ///     that each subscription flowed, and checking the build outputs from that subscription. If a dependency is not covered
    ///     by any output in the set, then we may be missing a subscription.  If a subscription does not cover any
    ///     dependencies in the repo, then that subscription may be superfluous. If the same dependency is covered by two separate
    ///     subscriptions, then we have a conflict.
    ///     
    ///     The difficulty here is that the because we do not know what the *next* build will produce, this metric is always
    ///     an approximation of future state. But, since what dependencies a repository produces changes relatively infrequently, it's
    ///     a generally good steady state measure.
    /// </summary>
    public class SubscriptionHealthMetric : HealthMetric
    {
        public SubscriptionHealthMetric(string repo, string branch, Func<DependencyDetail, bool> dependencySelector, ILogger logger, IRemoteFactory remoteFactory)
            : base(logger, remoteFactory)
        {
            Repository = repo;
            Branch = branch;
            DependenciesThatDoNotFlow = Enumerable.Empty<DependencyDetail>();
            DependenciesMissingSubscriptions = Enumerable.Empty<DependencyDetail>();
            ConflictingSubscriptions = Enumerable.Empty<SubscriptionConflict>();
            UnusedSubscriptions = Enumerable.Empty<Subscription>();
            DependencySelector = dependencySelector;
        }

        public readonly string Repository;
        public readonly string Branch;
        public readonly Func<DependencyDetail, bool> DependencySelector;
        public List<Subscription> Subscriptions { get; private set; }
        public List<DependencyDetail> Dependencies { get; private set; }

        public IEnumerable<SubscriptionConflict> ConflictingSubscriptions { get; private set; }

        /// <summary>
        ///     Dependencies that are missing subscriptions
        /// </summary>
        public IEnumerable<DependencyDetail> DependenciesMissingSubscriptions { get; private set; }

        /// <summary>
        ///     Dependencies with subscriptions that do not flow (disabled or 'none' update frequency)
        /// </summary>
        public IEnumerable<DependencyDetail> DependenciesThatDoNotFlow { get; private set; }

        /// <summary>
        ///     Subscriptions that are not used;
        /// </summary>
        public IEnumerable<Subscription> UnusedSubscriptions { get; private set; }

        /// <summary>
        ///     True if the version details file is missing from the repo+branch,
        ///     if this repo+branch is targeted by subscriptions.
        /// </summary>
        public bool MissingVersionDetailsFile { get; private set; }

        public override string MetricName => "Subscription Health";

        public override string MetricDescription => $"Subscription health for {Repository} @ {Branch}";

        /// <summary>
        ///     Evaluate the metric.
        /// </summary>
        /// <param name="remoteFactory">Remote factory</param>
        /// <returns>True if the metric passed, false otherwise</returns>
        public override async Task EvaluateAsync()
        {
            IRemote remote = await RemoteFactory.GetRemoteAsync(Repository, Logger);

            Logger.LogInformation("Evaluating subscription health metrics for {repo}@{branch}", Repository, Branch);

            // Get suscriptions that target this repo/branch
            Subscriptions = (await remote.GetSubscriptionsAsync(targetRepo: Repository))
                .Where(s => s.TargetBranch.Equals(Branch, StringComparison.OrdinalIgnoreCase)).ToList();

            // Get the dependencies of the repository/branch. Skip pinned and subscriptions tied to another
            // dependency (coherent parent), as well as those not selected by the dependency selector.
            try
            {
                Dependencies = (await remote.GetDependenciesAsync(Repository, Branch))
                    .Where(d => !d.Pinned && string.IsNullOrEmpty(d.CoherentParentDependencyName))
                    .Where(d => DependencySelector(d))
                    .ToList();
            }
            catch (DependencyFileNotFoundException)
            {
                // When the dependency file is not found, then we're good as long as this repo is not
                // targeted by any subscriptions
                if (Subscriptions.Any())
                {
                    MissingVersionDetailsFile = true;
                    Result = HealthResult.Failed;
                    return;
                }
                else
                {
                    Result = HealthResult.Passed;
                    return;
                }
            }

            Dictionary<string, Subscription> latestAssets = await GetLatestAssetsAndComputeConflicts(remote);
            ComputeSubscriptionUse(latestAssets);

            // Determine the result. A conflict or missing subscription is an error.
            // A non-flowing subscription or unused subscription is a warning
            if (DependenciesMissingSubscriptions.Any() ||
                ConflictingSubscriptions.Any())
            {
                Result = HealthResult.Failed;
            }
            else if (UnusedSubscriptions.Any() || DependenciesThatDoNotFlow.Any())
            {
                Result = HealthResult.Warning;
            }
            else
            {
                Result = HealthResult.Passed;
            }
        }

        /// <summary>
        ///     Compute the use of subscriptions.
        /// </summary>
        /// <param name="latestAssets">Latest assets produced by each subscription</param>
        private void ComputeSubscriptionUse(Dictionary<string, Subscription> latestAssets)
        {
            HashSet<Subscription> unusedSubs = new HashSet<Subscription>(Subscriptions);
            List<DependencyDetail> dependenciesThatDoNotFlow = new List<DependencyDetail>();
            List<DependencyDetail> dependenciesMissingSubscriptions = new List<DependencyDetail>();

            foreach (DependencyDetail dependency in Dependencies)
            {
                // Find a subscription that produced this dependency
                // If no subscription produced this, then this is a dependency with a missing subscription
                // If a subscription was found then check to see whether it flows automatically

                if (latestAssets.TryGetValue(dependency.Name, out Subscription subscription))
                {
                    unusedSubs.Remove(subscription);

                    if (!subscription.Enabled || subscription.Policy.UpdateFrequency == UpdateFrequency.None)
                    {
                        dependenciesThatDoNotFlow.Add(dependency);
                    }

                    // All good, no issues.
                }
                else
                {
                    dependenciesMissingSubscriptions.Add(dependency);
                }
            }

            UnusedSubscriptions = unusedSubs;
            DependenciesThatDoNotFlow = dependenciesThatDoNotFlow;
            DependenciesMissingSubscriptions = dependenciesMissingSubscriptions;
        }

        /// <summary>
        ///     Get the latest assets that were produced by each subscription
        ///     and compute any conflicts between subscriptionss
        /// </summary>
        /// <returns>Mapping of assets to subscriptions that produce them.</returns>
        private async Task<Dictionary<string, Subscription>> GetLatestAssetsAndComputeConflicts(IRemote remote)
        {
            // Populate the latest build task for each of these. The search for assets would be N*M*A where N is the number of
            // dependencies, M is the number of subscriptions, and A is average the number of assets per build.
            // Because this could add up pretty quickly, we build up a dictionary of assets->List<(subscription, build)>
            // instead.
            Dictionary<string, Subscription> assetsToLatestInSubscription =
                new Dictionary<string, Subscription>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, SubscriptionConflict> subscriptionConflicts = new Dictionary<string, SubscriptionConflict>();

            foreach (Subscription subscription in Subscriptions)
            {
                // Look up the latest build and add it to the dictionary.
                Build latestBuild = await remote.GetLatestBuildAsync(subscription.SourceRepository, subscription.Channel.Id);

                if (latestBuild != null)
                {
                    foreach (var asset in latestBuild.Assets)
                    {
                        string assetName = asset.Name;

                        if (assetsToLatestInSubscription.TryGetValue(assetName, out Subscription otherSubscription))
                        {
                            // Repos can publish the same asset twice for the same uild, so filter out those cases,
                            // as well as cases where the subscription is functionally the same (e.g. you have a twice daily
                            // and weekly subscription). Basically cases where the source repo and source channels are the same.

                            if (otherSubscription.SourceRepository.Equals(subscription.SourceRepository, StringComparison.OrdinalIgnoreCase) &&
                                otherSubscription.Channel.Id == subscription.Channel.Id)
                            {
                                continue;
                            }

                            // While technically this asset would need to be utilized in the dependencies
                            // to cause an issue, it's an issue waiting to happen, so stick this in the conflicting subscriptions.
                            if (subscriptionConflicts.TryGetValue(assetName, out SubscriptionConflict conflict))
                            {
                                conflict.Subscriptions.Add(subscription);
                            }
                            else
                            {
                                SubscriptionConflict newConflict = new SubscriptionConflict(assetName,
                                    new List<Subscription>() { otherSubscription, subscription },
                                    Dependencies.Any(d => d.Name.Equals(assetName, StringComparison.OrdinalIgnoreCase)));
                                subscriptionConflicts.Add(assetName, newConflict);
                            }
                        }
                        else
                        {
                            assetsToLatestInSubscription.Add(assetName, subscription);
                        }
                    }
                }
            }

            // Now there is a complete accounting of the conflicts.
            ConflictingSubscriptions = subscriptionConflicts.Values.ToList();

            return assetsToLatestInSubscription;
        }
    }

    public class SubscriptionConflict
    {
        public SubscriptionConflict(string asset, List<Subscription> subscriptions, bool utilized)
        {
            Asset = asset;
            Subscriptions = subscriptions;
            Utilized = utilized;
        }

        /// <summary>
        ///     Asset/Dependency name that is produced by two subscriptions at latest
        /// </summary>
        public readonly string Asset;
        /// <summary>
        ///     Subscriptions that produce the asset
        /// </summary>
        public readonly List<Subscription> Subscriptions;
        /// <summary>
        ///     True if this conflict will be seen by a 'live' dependency (within the target repo/branch).
        /// </summary>
        public readonly bool Utilized;
    }
}
