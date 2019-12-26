using System.Collections.Generic;
using System.Linq;
using Maestro.Data;
using Maestro.Data.Models;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.HealthMetrics;

namespace Maestro.Web
{
    public class SubscriptionDependencyDetails
    {
        public readonly string Repository;
        public readonly string Branch;

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
        ///     Subscriptions that are not used
        /// </summary>
        public IEnumerable<Subscription> UnusedSubscriptions { get; private set; }

        public SubscriptionDependencyDetails()
        { }

        public SubscriptionDependencyDetails(SubscriptionHealthMetric subHealthMetric)
        {
            Repository = subHealthMetric.Repository;
            Branch = subHealthMetric.Branch;
            Subscriptions = subHealthMetric.Subscriptions.Select(s => ModelTranslators.ClientToDataModel_Subscription(s)).ToList();
            Dependencies = subHealthMetric.Dependencies;
            ConflictingSubscriptions = subHealthMetric.ConflictingSubscriptions;
            DependenciesMissingSubscriptions = subHealthMetric.DependenciesMissingSubscriptions;
            DependenciesThatDoNotFlow = subHealthMetric.DependenciesThatDoNotFlow;
            UnusedSubscriptions = subHealthMetric.UnusedSubscriptions.Select(s => ModelTranslators.ClientToDataModel_Subscription(s)).ToList();
        }
    }
}
