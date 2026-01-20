// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
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
    } while (beforeTimestamp > status?.ComputationTimestamp);

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

    foreach (var (branchName, branchStatus) in status.BranchStatuses)
    {
        Console.WriteLine($"Branch: {branchName}");
        Console.WriteLine($"  Default Channel ID: {branchStatus.DefaultChannelId}");

        if (branchStatus.SubscriptionStatuses == null || branchStatus.SubscriptionStatuses.Count == 0)
        {
            Console.WriteLine("  No subscription statuses.");
        }
        else
        {
            Console.WriteLine("  Subscriptions:");
            foreach (var subStatus in branchStatus.SubscriptionStatuses)
            {
                Console.WriteLine($"    - Subscription ID: {subStatus.SubscriptionId}");
                Console.WriteLine($"      Target Repository: {subStatus.TargetRepository}");
                Console.WriteLine($"      Target Branch: {subStatus.TargetBranch}");
                Console.WriteLine($"      Commits Distance: {subStatus.CommitDistance}");
                Console.WriteLine($"      Last Backflowed Commit: {subStatus.LastBackflowedSha}");
                Console.WriteLine();
            }
        }
    }
}

[Verb("trigger", HelpText = "Trigger backflow status calculation for a given build")]
file class TriggerOptions : Options
{
}

[Verb("show", HelpText = "Show backflow status information for a given build")]
file class ShowOptions : Options
{
}

abstract file class Options
{
    [Option("build", Required = true, HelpText = "The VMR build ID")]
    public int Build { get; set; }

    [Option("pcs-uri", Required = false, HelpText = "PCS base URI, defaults to production")]
    public string? PcsUri { get; set; }
}
