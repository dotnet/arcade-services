using Microsoft.AspNetCore.Mvc;

namespace BuildInsights.DummyApp.Controllers;

[ApiController]
public class HealthController : ControllerBase
{
    [HttpGet("/health")]
    public IActionResult Health() => Ok("ok");

    [HttpGet("/alive")]
    public IActionResult Alive() => Ok("ok");
}
