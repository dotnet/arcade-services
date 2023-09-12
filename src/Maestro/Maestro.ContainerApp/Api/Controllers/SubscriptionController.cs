// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Maestro.ContainerApp.Queues;
using Maestro.ContainerApp.Queues.WorkItems;
using Microsoft.AspNetCore.Mvc;

namespace Maestro.ContainerApp.Api.Controllers;

[ApiController]
[Route("[controller]")]
public class SubscriptionController : ControllerBase
{
    private readonly ILogger<SubscriptionController> _logger;
    private readonly QueueProducerFactory _queueClientFactory;

    public SubscriptionController(ILogger<SubscriptionController> logger, QueueProducerFactory queueClientFactory)
    {
        _logger = logger;
        _queueClientFactory = queueClientFactory;
    }

    /// <summary>
    ///   Trigger a <see cref="Subscription"/> manually by id
    /// </summary>
    /// <param name="id">The id of the <see cref="Subscription"/> to trigger.</param>
    /// <param name="buildId">'bar-build-id' if specified, a specific build is requested</param>
    [HttpPost("{id}/trigger")]
    public virtual async Task<IActionResult> TriggerSubscription(Guid id, [FromQuery(Name = "bar-build-id")] int buildId = 0)
    {
        var client = _queueClientFactory.Create<StartSubscriptionUpdateWorkItem>();
        await client.SendAsync(new StartSubscriptionUpdateWorkItem
        {
            SubscriptionId = id,
            BuildId = buildId,
        });

        _logger.LogInformation($"Requested subscription trigger for {id}");

        return Ok();
    }
}
