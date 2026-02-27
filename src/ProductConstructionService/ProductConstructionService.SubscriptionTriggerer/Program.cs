// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Maestro.Common;
using Maestro.Data.Models;
using Microsoft.ApplicationInsights.Channel;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ProductConstructionService.SubscriptionTriggerer;
using ProductConstructionService.WorkItems;

if (args.Length < 1)
{
    Console.WriteLine("Usage: SubscriptionTriggerer <daily|twicedaily|weekly|everytwoweeks|everymonth>");
    return 1;
}

InMemoryChannel telemetryChannel = new();
UpdateFrequency frequency = args[0] switch
{
    "daily" => UpdateFrequency.EveryDay,
    "twicedaily" => UpdateFrequency.TwiceDaily,
    "weekly" => UpdateFrequency.EveryWeek,
    "everytwoweeks" => UpdateFrequency.EveryTwoWeeks,
    "everymonth" => UpdateFrequency.EveryMonth,
    _ => throw new ArgumentException($"Invalid frequency ${args[0]} specified")
};

try
{
    var builder = Host.CreateApplicationBuilder();

    builder.ConfigureSubscriptionTriggerer(telemetryChannel);

    // We're registering BAR context as a scoped service, so we have to create a scope to resolve it
    var applicationScope = builder.Build().Services.CreateScope();

    if (builder.Environment.IsDevelopment())
    {
        var config = applicationScope.ServiceProvider.GetRequiredService<IConfiguration>();
        await applicationScope.ServiceProvider.UseLocalWorkItemQueues(
        [
            builder.Configuration.GetRequiredValue("DefaultWorkItemQueueName"),
            builder.Configuration.GetRequiredValue("CodeflowWorkItemQueueName"),
        ]);
    }

    var triggerer = applicationScope.ServiceProvider.GetRequiredService<SubscriptionTriggerer>();

    await triggerer.TriggerSubscriptionsAsync(frequency);
}
finally
{
    telemetryChannel.Flush();
    await Task.Delay(TimeSpan.FromMilliseconds(1000));
}

return 0;
