// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ProductConstructionService.Api.Queue;
using ProductConstructionService.Api.Queue.Jobs;

namespace ProductConstructionService.Api;
[Route("test")]
public class TestController(JobProducerFactory pcsJobProducerFactory) : Controller
{
    private readonly JobProducerFactory _jobProducerFactory = pcsJobProducerFactory;

    [AllowAnonymous]
    [HttpPost("1")]
    public async Task<IActionResult> Index()
    {
        var jobProducer = _jobProducerFactory.Create<TextJob>();
        await jobProducer.ProduceJobAsync(new() { Text = "some text" });
        return Ok("Message sent");
    }
}
