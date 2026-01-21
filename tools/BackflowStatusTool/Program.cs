// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Linq;
using System.Threading.Tasks;
using CommandLine;
using Microsoft.DotNet.ProductConstructionService.Client;
using Microsoft.DotNet.ProductConstructionService.Client.Models;

return await Parser.Default.ParseArguments<TriggerOptions, ShowOptions>(args)
    .MapResult(
        async (TriggerOptions opts) => await TriggerBackflowStatusAsync(opts),
        async (ShowOptions opts) => await ShowBackflowStatusAsync(opts),
        _ => Task.FromResult(1));

async Task<int> TriggerBackflowStatusAsync(TriggerOptions opts)
{
    var api = opts.PcsUri is not null
        ? PcsApiFactory.GetAuthenticated(opts.PcsUri, null, null, false)
        : PcsApiFactory.GetAuthenticated(null, null, false);

    var beforeTimestamp = DateTimeOffset.UtcNow;
    Console.WriteLine($"Triggering backflow status calculation for build {opts.Build}...");

    await api.BackflowStatus.TriggerBackflowStatusCalculationAsync(vmrBuildId: opts.Build);

    Console.WriteLine("Waiting for calculation to complete...");

    BackflowStatus? status = null;
    do
    {
        await Task.Delay(TimeSpan.FromSeconds(10));
        try
        {
            status = await api.BackflowStatus.GetBackflowStatusAsync(vmrBuildId: opts.Build);
        }
        catch (RestApiException e) when (e.Response.Status == 404)
        {
            continue;
        }
    } while (status == null || beforeTimestamp > status.ComputationTimestamp);

    Console.WriteLine($"Backflow status calculation completed at {status!.ComputationTimestamp:u}");
    PrintStatus(status);

    return 0;
}

async Task<int> ShowBackflowStatusAsync(ShowOptions opts)
{
    var api = opts.PcsUri is not null
        ? PcsApiFactory.GetAuthenticated(opts.PcsUri, null, null, false)
        : PcsApiFactory.GetAuthenticated(null, null, false);

    Console.WriteLine($"Fetching backflow status for build {opts.Build}...");

    BackflowStatus? status;
    try
    {
        status = await api.BackflowStatus.GetBackflowStatusAsync(vmrBuildId: opts.Build);
    }
    catch (RestApiException e) when (e.Response.Status == 404)
    {
        status = null;
    }

    if (status == null)
    {
        Console.WriteLine("No backflow status found (status may not have been calculated yet).");
        Console.WriteLine($"Hint: Run 'trigger --build {opts.Build}' to start the calculation.");
        return 0;
    }

    PrintStatus(status);
    return 0;
}

void PrintStatus(BackflowStatus status)
{
    Console.WriteLine();
    Console.WriteLine($"VMR Commit SHA: {status.VmrCommitSha}");
    Console.WriteLine($"Computation Timestamp: {status.ComputationTimestamp:u}");
    Console.WriteLine();

    if (status.BranchStatuses == null || status.BranchStatuses.Count == 0)
    {
        Console.WriteLine("No branch statuses available.");
        return;
    }

    // Collect all subscription statuses
    var allStatuses = status.BranchStatuses
        .SelectMany(b => b.Value.SubscriptionStatuses ?? [])
        .ToList();

    if (allStatuses.Count == 0)
    {
        Console.WriteLine("No subscription statuses available.");
        return;
    }

    // Calculate column widths
    int repoWidth = Math.Max("Target Repository".Length, allStatuses.Max(s => s.TargetRepository?.Length ?? 0));
    int branchWidth = Math.Max("Target Branch".Length, allStatuses.Max(s => s.TargetBranch?.Length ?? 0));
    int shaWidth = Math.Max("Backflown SHA".Length, 40);
    int distanceWidth = Math.Max("Commit Distance".Length, 15);

    // Print header
    Console.WriteLine($"{"Target Repository".PadRight(repoWidth)} | {"Target Branch".PadRight(branchWidth)} | {"Backflown SHA".PadRight(shaWidth)} | {"Commit Distance".PadRight(distanceWidth)}");
    Console.WriteLine(new string('-', repoWidth + branchWidth + shaWidth + distanceWidth + 9));

    // Print rows
    foreach (var subStatus in allStatuses.OrderBy(s => s.CommitDistance).ThenBy(s => s.TargetRepository).ThenBy(s => s.TargetBranch))
    {
        var repo = (subStatus.TargetRepository ?? "").PadRight(repoWidth);
        var branch = (subStatus.TargetBranch ?? "").PadRight(branchWidth);
        var sha = (subStatus.LastBackflowedSha ?? "").PadRight(shaWidth);
        var distance = subStatus.CommitDistance.ToString();

        Console.Write($"{repo} | {branch} | ");

        // Print SHA with color
        PrintWithDistanceColor(sha, subStatus.CommitDistance);
        Console.Write(" | ");

        // Print distance with color
        PrintWithDistanceColor(distance.PadRight(distanceWidth), subStatus.CommitDistance);
        Console.WriteLine();
    }
}

void PrintWithDistanceColor(string text, int? distance)
{
    var originalColor = Console.ForegroundColor;

    if (distance.HasValue)
    {
        Console.ForegroundColor = distance.Value switch
        {
            < 5 => ConsoleColor.Green,
            < 20 => ConsoleColor.Yellow,
            _ => ConsoleColor.Red
        };
    }

    Console.Write(text);
    Console.ForegroundColor = originalColor;
}

/// <summary>
/// Command-line options for the <c>trigger</c> verb.
/// </summary>
/// <remarks>
/// The <c>trigger</c> command starts a backflow status calculation for the specified VMR build
/// and waits until the computation completes before returning.
/// </remarks>
[Verb("trigger", HelpText = "Trigger backflow status calculation for a given build")]
file class TriggerOptions : Options
{
}

/// <summary>
/// Command-line options for the <c>show</c> verb.
/// </summary>
/// <remarks>
/// The <c>show</c> command retrieves and displays the current backflow status for the specified
/// VMR build. If no status is available yet, it suggests running the <c>trigger</c> command.
/// </remarks>
[Verb("show", HelpText = "Show backflow status information for a given build")]
file class ShowOptions : Options
{
}

/// <summary>
/// Common command-line options shared by all Backflow Status Tool verbs.
/// </summary>
/// <remarks>
/// Typical usage:
/// <code>
///   backflow-status trigger --build 12345
///   backflow-status show --build 12345
/// </code>
/// </remarks>
abstract file class Options
{
    /// <summary>
    /// Gets or sets the VMR build ID for which backflow status should be calculated or displayed.
    /// </summary>
    [Option("build", Required = true, HelpText = "The VMR build ID")]
    public int Build { get; set; }

    /// <summary>
    /// Gets or sets the Product Construction Service (PCS) base URI.
    /// </summary>
    /// <remarks>
    /// If not specified, the tool uses the default production PCS endpoint.
    /// </remarks>
    [Option("pcs-uri", Required = false, HelpText = "PCS base URI, defaults to production")]
    public string? PcsUri { get; set; }
}
