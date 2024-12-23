// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.Extensions.Logging;

namespace ProductConstructionService.ReproTool;

internal class ReproTool(
    IBarApiClient barClient,
    ReproToolOptions options,
    IProcessManager processManager,
    ILogger<ReproTool> logger)
{
    internal async Task ReproduceCodeFlow()
    {
        logger.LogInformation("Fetching {subscriptionId} subscription from BAR",
            options.Subscription);
        var subscription = await barClient.GetSubscriptionAsync(options.Subscription);

        if (subscription == null)
        {
            throw new ArgumentException($"Couldn't find subscription with subscription id {options.Subscription}");
        }

        if (!subscription.SourceEnabled)
        {
            throw new ArgumentException($"Subscription {options.Subscription} is not a code flow subscription");
        }

        if (!string.IsNullOrEmpty(subscription.SourceDirectory) && !string.IsNullOrEmpty(subscription.TargetDirectory))
        {
            throw new ArgumentException($"Code flow subscription incorrectly configured: is missing SourceDirectory or TargetDirectory");
        }

        var ghCliPath = await Helpers.Which("gh");
        var res = await processManager.Execute(
            ghCliPath,
            [
                "repo",
                "view",
                "https://github.com/dotnet/arcade-services"
            ]);
        Console.WriteLine(res.StandardOutput);
    }
}
