// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using CommandLine;
using Microsoft.DotNet.Darc.Operations;

namespace Microsoft.DotNet.Darc.Options;

[Verb("get-subscription-history", HelpText = "Get the history of subscription trigger outcomes matching the given filters.")]
internal class GetSubscriptionHistoryCommandLineOptions : CommandLineOptions<GetSubscriptionHistoryOperation>
{
    [Option("id", HelpText = "Filter by subscription id (GUID).")]
    public string SubscriptionId { get; set; }

    [Option("build", HelpText = "Filter by build id.")]
    public int? BuildId { get; set; }

    [Option("type", HelpText = "Filter by outcome type. Valid values: Updated, NoUpdate, NotUpdatable, Failure, UserError, HasConflict, Rescheduled.")]
    public string OutcomeType { get; set; }

    [Option("search", HelpText = "Free-text filter matched against the subscription's source repo, target repo and target branch.")]
    public string Search { get; set; }

    [Option("after", HelpText = "Return only outcomes on or after this date (e.g. \"2025-01-15T12:00:00Z\").")]
    public string After { get; set; }

    [Option("before", HelpText = "Return only outcomes on or before this date (e.g. \"2025-01-15T12:00:00Z\").")]
    public string Before { get; set; }

    [Option("limit", Default = 10, HelpText = "Maximum number of outcomes to return (1-1000).")]
    public int Limit { get; set; } = 10;

    public override bool IsOutputFormatSupported()
        => OutputFormat switch
        {
            DarcOutputType.json => true,
            _ => base.IsOutputFormatSupported(),
        };
}
