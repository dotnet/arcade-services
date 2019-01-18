// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using CommandLine;
using System.Text.RegularExpressions;
using System;

namespace Microsoft.DotNet.Darc.Options
{
    abstract class SubscriptionsCommandLineOptions : CommandLineOptions
    {
        [Option("target-repo", HelpText = "Filter by target repo (matches substring unless --exact or --regex is passd).")]
        public string TargetRepository { get; set; }

        [Option("source-repo", HelpText = "Filter by source repo (matches substring unless --exact or --regex is passd).")]
        public string SourceRepository { get; set; }

        [Option("channel", HelpText = "Filter by source channel (matches substring unless --exact or --regex is passd).")]
        public string Channel { get; set; }

        [Option("target-branch", HelpText = "Filter by target branch (matches substring unless --exact or --regex is passd).")]
        public string TargetBranch { get; set; }

        [Option("exact", SetName = "exact", HelpText = "Match subscription parameters exactly (cannot be used with --regex).")]
        public bool ExactMatch { get; set; }

        [Option("regex", SetName = "regex", HelpText = "Match subscription parameters using regex (cannot be used with --exact).")]
        public bool RegexMatch { get; set; }

        public bool SubscriptionParameterMatches(string inputParameter, string subscriptionProperty)
        {
            if (string.IsNullOrEmpty(inputParameter))
            {
                return true;
            }
            else if (ExactMatch && inputParameter == subscriptionProperty)
            {
                return true;
            }
            else if (RegexMatch && Regex.IsMatch(subscriptionProperty, inputParameter))
            {
                return true;
            }
            else
            {
                return subscriptionProperty.Contains(inputParameter, StringComparison.OrdinalIgnoreCase);
            }
        }
    }
}
