using Microsoft.AspNetCore.Mvc;

namespace BuildInsights.DummyApp.Controllers;

[ApiController]
[Route("/status")]
public class StatusController : ControllerBase
{
    [HttpGet]
    public IActionResult GetStatus()
    {
        return Ok(new
        {
            Status = "Healthy",
            Timestamp = DateTimeOffset.UtcNow
        });
    }
}
