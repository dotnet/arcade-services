// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
using Maestro.Data.Models;
using Microsoft.ApplicationInsights.Channel;
using Microsoft.Extensions.DependencyInjection;
using SubscriptionTriggerer;

Console.WriteLine("Hello, World!");
Console.WriteLine(string.Join(" ", args));

InMemoryChannel telemetryChannel = new();
UpdateFrequency frequency = args[0] switch
{
    "daily" => UpdateFrequency.EveryDay,
    "twicedaily" => UpdateFrequency.TwiceDaily,
    "weekly" => UpdateFrequency.EveryWeek,
    _ => throw new ArgumentException($"Invalid frequency ${args[0]} specified")
};

try
{
    ServiceCollection services = new();

    SubscriptionTriggererConfiguration.RegisterServices(services, telemetryChannel);

    ServiceProvider serviceProvider = services.BuildServiceProvider();

    var triggerer = ActivatorUtilities.CreateInstance<SubscriptionTriggerer.SubscriptionTriggerer>(serviceProvider);

    await triggerer.CheckSubscriptionsAsync(frequency);
}
finally
{
    telemetryChannel.Flush();
    await Task.Delay(TimeSpan.FromMilliseconds(1000));
}
