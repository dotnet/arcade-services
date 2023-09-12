// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Maestro.ContainerApp.Queues.WorkItems;

internal class StartSubscriptionUpdateWorkItem : BackgroundWorkItem
{
    public Guid SubscriptionId { get; set; }

    public int BuildId { get; set; }
}
