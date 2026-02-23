using Microsoft.AspNetCore.Mvc;

namespace AuthHub.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AboutController : ControllerBase
{
    [HttpGet]
    public IActionResult Get()
    {
        return Ok(new
        {
            name = "AuthHub API",
            version = "1.0.0",
            description = "JWT + Refresh Token authentication service",
            author = "SeninAdın",
            timestamp = DateTime.UtcNow
        });
    }
}