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
public class TestController(PcsJobProducerFactory pcsJobProducerFactory) : Controller
{
    private readonly PcsJobProducerFactory _pcsJobProducerFactory = pcsJobProducerFactory;

    [HttpGet("1")]
    public async Task<IActionResult> Index()
    {
        var queueInjector = _pcsJobProducerFactory.Create<TextPcsJob>();
        await queueInjector.ProduceJobAsync(new() { Text = "some text"});
        return Ok("Message sent");
    }
}
