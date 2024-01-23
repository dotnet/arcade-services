// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text.Json;
using Azure.Storage.Queues;
using Maestro.Data;
using Microsoft.AspNetCore.Mvc;
using ProductConstructionService.Api.Queue;
using ProductConstructionService.Api.Queue.WorkItems;

namespace ProductConstructionService.Api;
[Route("test")]
public class TestController(
    BuildAssetRegistryContext dbContext,
    PcsJobProducerFactory pcsJobProducerFactory,
    QueueServiceClient client,
    PcsJobsProcessorStatus status) : Controller
{
    private readonly PcsJobProducerFactory _pcsJobProducerFactory = pcsJobProducerFactory;
    private readonly BuildAssetRegistryContext _dbContext = dbContext;

    [HttpGet("1")]
    public async Task<IActionResult> Index()
    {
        var queueInjector = _pcsJobProducerFactory.Create<TextPcsJob>();
        await queueInjector.ProduceJobAsync(new() { Text = "some text"});
        return Ok("Message sent");
    }

    [HttpGet("2")]
    public IActionResult Something()
    {
        var q = client.GetQueueClient("pcs-jobs");
        var message = q.ReceiveMessage();
        var a = JsonSerializer.Deserialize<TextPcsJob>(message.Value.Body);
        status.ContinueWorking = false;
        while(!status.StoppedWorking)
        {
            Thread.Sleep(2000);
        }
        
        return Ok("Done!");
    }
}
