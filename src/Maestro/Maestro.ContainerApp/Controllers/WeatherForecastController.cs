// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Azure.Identity;
using Azure.Storage.Queues;
using Azure.Storage;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Azure;

namespace Maestro.ContainerApp.Controllers;
[ApiController]
[Route("[controller]")]
public class WeatherForecastController : ControllerBase
{
    private readonly ILogger<WeatherForecastController> _logger;
    private readonly QueueServiceClient _queueClient;

    public WeatherForecastController(ILogger<WeatherForecastController> logger, QueueServiceClient queueClient)
    {
        _logger = logger;
        _queueClient = queueClient;
    }

    [HttpGet(Name = "GetWeatherForecast")]
    public async Task<IActionResult> Get()
    {
        var client = await _queueClient.CreateQueueAsync("new-queue");
        await client.Value.SendMessageAsync("Hello, Azure!");
        return Ok();
    }
}
