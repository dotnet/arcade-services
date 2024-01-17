// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Azure.Storage.Queues;
using Maestro.Data;
using Microsoft.AspNetCore.Mvc;
using ProductConstructionService.Api.Queue;
using ProductConstructionService.Api.Queue.WorkItems;

namespace ProductConstructionService.Api;
[Route("test")]
public class TestController(BuildAssetRegistryContext dbContext, QueueMessageSenderFactory queueInjectorFactory, QueueServiceClient client) : Controller
{
    private readonly QueueMessageSenderFactory _queueInjectorFactory = queueInjectorFactory;
    private readonly BuildAssetRegistryContext _dbContext = dbContext;

    [HttpGet("1")]
    public async Task<IActionResult> Index()
    {
        var queueInjector = _queueInjectorFactory.Create<TextWorkitem>();
        await queueInjector.SendAsync(new("some message"));
        return Ok("Message sent");
    }

    [HttpGet("2")]
    public IActionResult Something()
    {
        var q = client.GetQueueClient("workitem-queue");
        var message = q.ReceiveMessage();
        return Ok(message.Value.Body.ToString());
    }
}
