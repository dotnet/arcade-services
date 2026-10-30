// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.DotNet.Darc.Operations;

namespace Microsoft.DotNet.Darc.Options;

internal class GetSubscriptionHistoryCommandLineOptions : CommandLineOptions<GetSubscriptionHistoryOperation>
{
    public override string Verb => "get-subscription-history";

    public override string VerbDescription => "Get the history of subscription trigger outcomes matching the given filters.";

    public string SubscriptionId { get; set; }

    public int? BuildId { get; set; }

    public string OutcomeType { get; set; }

    public string Search { get; set; }

    public string After { get; set; }

    public string Before { get; set; }

    public int Limit { get; set; } = 10;

    protected override void Configure()
    {
        Add<string>("--id", "Filter by subscription id (GUID).", value => SubscriptionId = value);
        Add<int?>("--build", "Filter by build id.", value => BuildId = value);
        Add<string>("--type", "Filter by outcome type. Valid values: Updated, NoUpdate, NotUpdatable, Failure, UserError, HasConflict, Rescheduled.", value => OutcomeType = value);
        Add<string>("--search", "Free-text filter matched against the subscription's source repo, target repo and target branch.", value => Search = value);
        Add<string>("--after", "Return only outcomes on or after this date (e.g. \"2025-01-15T12:00:00Z\").", value => After = value);
        Add<string>("--before", "Return only outcomes on or before this date (e.g. \"2025-01-15T12:00:00Z\").", value => Before = value);
        Add<int>("--limit", "Maximum number of outcomes to return (1-1000).", value => Limit = value, defaultValue: () => 10);
        base.Configure();
    }

    public override bool IsOutputFormatSupported()
        => OutputFormat switch
        {
            DarcOutputType.json => true,
            _ => base.IsOutputFormatSupported(),
        };
}
