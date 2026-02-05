using ArbitrageApi.Models;
using ArbitrageApi.Services;
using Microsoft.AspNetCore.Mvc;

namespace ArbitrageApi.Controllers;

[ApiController]
[Route("api/statistics")]
public class StatsController : ControllerBase
{
    private readonly ArbitrageStatsService _statsService;
    private readonly ArbitrageExportService _exportService;
    private readonly ILogger<StatsController> _logger;

    public StatsController(
        ArbitrageStatsService statsService, 
        ArbitrageExportService exportService,
        ILogger<StatsController> logger)
    {
        _statsService = statsService;
        _exportService = exportService;
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

    [HttpGet("cell-details")]
    public async Task<ActionResult> GetCellDetails([FromQuery] string day, [FromQuery] int hour)
    {
        try
        {
            var cell = await _statsService.GetCellDetailsAsync(day, hour);
            var events = await _statsService.GetCellEventsAsync(day, hour);
            
            return Ok(new {
                Summary = cell ?? new HeatmapCell { Day = day, Hour = hour },
                Events = events
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving cell details for {Day} {Hour}", day, hour);
            return StatusCode(500, "An error occurred while retrieving cell details");
        }
    }

    [HttpGet("events/{pair}")]
    public async Task<ActionResult<List<ArbitrageEvent>>> GetEventsByPair(string pair)
    {
        try
        {
            var events = await _statsService.GetEventsByPairAsync(pair);
            return Ok(events);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving events for pair {Pair}", pair);
            return StatusCode(500, "An error occurred while retrieving events");
        }
    }

    [HttpGet("cell-events-all")]
    public async Task<ActionResult<List<ArbitrageEvent>>> GetAllCellEvents([FromQuery] string day, [FromQuery] int hour)
    {
        try
        {
            var events = await _statsService.GetAllCellEventsAsync(day, hour);
            return Ok(events);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving all cell events for {Day} {Hour}", day, hour);
            return StatusCode(500, "An error occurred while retrieving full cell history");
        }
    }

    [HttpGet("export-zipped")]
    public async Task<IActionResult> ExportZipped([FromQuery] string day, [FromQuery] int hour)
    {
        try
        {
            var data = await _exportService.ExportCellEventsToZipAsync(day, hour);
            var fileName = $"Arbitrage_Activity_{day}_{hour:D2}-00.zip";
            return File(data, "application/zip", fileName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error exporting zipped data for {Day} {Hour}", day, hour);
            return StatusCode(500, "An error occurred while generating export");
        }
    }
}
