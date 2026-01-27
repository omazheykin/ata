using ArbitrageApi.Models;
using ArbitrageApi.Services;
using Microsoft.AspNetCore.Mvc;

namespace ArbitrageApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class StatsController : ControllerBase
{
    private readonly ArbitrageStatsService _statsService;
    private readonly ILogger<StatsController> _logger;

    public StatsController(ArbitrageStatsService statsService, ILogger<StatsController> logger)
    {
        _statsService = statsService;
        _logger = logger;
    }

    [HttpGet]
    public async Task<ActionResult<StatsResponse>> GetStats()
    {
        try
        {
            var stats = await _statsService.GetStatsAsync();
            return Ok(stats);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving statistics");
            return StatusCode(500, "An error occurred while retrieving statistics");
        }
    }
}
