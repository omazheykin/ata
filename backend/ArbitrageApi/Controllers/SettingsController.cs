using ArbitrageApi.Services;
using Microsoft.AspNetCore.Mvc;

namespace ArbitrageApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SettingsController : ControllerBase
{
    private readonly ArbitrageDetectionService _detectionService;

    public SettingsController(ArbitrageDetectionService detectionService)
    {
        _detectionService = detectionService;
    }

    [HttpGet("sandbox")]
    public ActionResult GetSandboxMode()
    {
        return Ok(new { Enabled = _detectionService.IsSandboxMode });
    }

    [HttpPost("sandbox")]
    public async Task<ActionResult> SetSandboxMode([FromQuery] bool enabled)
    {
        await _detectionService.SetSandboxMode(enabled);
        return Ok(new { Enabled = enabled });
    }
}
