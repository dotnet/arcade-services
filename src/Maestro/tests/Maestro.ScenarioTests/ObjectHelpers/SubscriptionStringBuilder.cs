using System.Text;
using Microsoft.DotNet.Darc;
using Microsoft.DotNet.Maestro.Client.Models;

namespace Maestro.ScenarioTests.ObjectHelpers
{
    /// <summary>
    /// Contains the data for a subscription object and the logic for formatting into a darc return string, based on the code in Darc.GetSubscriptionsOperation
    /// </summary>
    public class SubscriptionStringBuilder
    {
        public static string GetSubscriptionString(Subscription subscription, string mergePoliciesOutput = null)
        {
            StringBuilder subInfo = new StringBuilder();

            subInfo.AppendLine($"{subscription.SourceRepository} ({subscription.Channel.Name}) ==> '{subscription.TargetRepository}' ('{subscription.TargetBranch}')");
            subInfo.AppendLine($"  - Id: {subscription.Id}");
            subInfo.AppendLine($"  - Update Frequency: {subscription.Policy.UpdateFrequency}");
            subInfo.AppendLine($"  - Enabled: {subscription.Enabled}");
            subInfo.AppendLine($"  - Batchable: {subscription.Policy.Batchable}");

            string mergePolicies = mergePoliciesOutput ?? UxHelpers.GetMergePoliciesDescription(subscription.Policy.MergePolicies, "  ").TrimEnd('\r', '\n');
            subInfo.AppendLine(mergePolicies);

            // Currently the API only returns the last applied build for requests to specific subscriptions.
            // This will be fixed, but for now, don't print the last applied build otherwise.
            if (subscription.LastAppliedBuild != null)
            {
                subInfo.AppendLine($"  - Last Build: {subscription.LastAppliedBuild.AzureDevOpsBuildNumber} ({subscription.LastAppliedBuild.Commit})");
            }

            return subInfo.ToString();
        }
    }
}
