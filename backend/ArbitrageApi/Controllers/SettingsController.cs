using ArbitrageApi.Services;
using Microsoft.AspNetCore.Mvc;

namespace ArbitrageApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SettingsController : ControllerBase
{
    private readonly ArbitrageDetectionService _detectionService;
    private readonly StatePersistenceService _persistenceService;

    public SettingsController(ArbitrageDetectionService detectionService, StatePersistenceService persistenceService)
    {
        _detectionService = detectionService;
        _persistenceService = persistenceService;
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

    [HttpGet("smart-strategy")]
    public ActionResult GetSmartStrategy()
    {
        return Ok(new { Enabled = _detectionService.IsSmartStrategyEnabled });
    }

    [HttpPost("smart-strategy")]
    public async Task<ActionResult> SetSmartStrategy([FromQuery] bool enabled)
    {
        await _detectionService.SetSmartStrategy(enabled);
        return Ok(new { Enabled = enabled });
    }

    [HttpGet("strategy-status")]
    public ActionResult GetStrategyStatus()
    {
        var (threshold, reason) = _detectionService.GetCurrentStrategy();
        return Ok(new { Threshold = threshold, Reason = reason });
    }

    [HttpGet("state")]
    public ActionResult GetState()
    {
        return Ok(_persistenceService.GetState());
    }

    [HttpPost("pair-thresholds")]
    public async Task<ActionResult> SetPairThresholds([FromBody] Dictionary<string, decimal> thresholds)
    {
        await _detectionService.SetPairThresholds(thresholds);
        return Ok(thresholds);
    }

    [HttpPost("safe-multiplier")]
    public async Task<ActionResult> SetSafeBalanceMultiplier([FromQuery] decimal multiplier)
    {
        await _detectionService.SetSafeBalanceMultiplier(multiplier);
        return Ok(new { Multiplier = multiplier });
    }

    [HttpPost("taker-fees")]
    public async Task<ActionResult> SetUseTakerFees([FromQuery] bool enabled)
    {
        await _detectionService.SetUseTakerFees(enabled);
        return Ok(new { Enabled = enabled });
    }

    [HttpPost("auto-rebalance")]
    public async Task<ActionResult> SetAutoRebalance([FromQuery] bool enabled)
    {
        await _detectionService.SetAutoRebalance(enabled);
        return Ok(new { Enabled = enabled });
    }

    [HttpPost("safety-reset")]
    public async Task<ActionResult> ResetSafetyKillSwitch()
    {
        await _detectionService.ResetSafetyKillSwitch();
        return Ok(new { Success = true });
    }

    [HttpPost("safety-limits")]
    public async Task<ActionResult> SetSafetyLimits([FromQuery] decimal drawdown, [FromQuery] int losses)
    {
        await _detectionService.SetSafetyLimits(drawdown, losses);
        return Ok(new { Drawdown = drawdown, Losses = losses });
    }

    [HttpPost("rebalance-threshold")]
    public async Task<ActionResult> SetRebalanceThreshold([FromQuery] decimal threshold)
    {
        await _detectionService.SetRebalanceThreshold(threshold);
        return Ok(new { Threshold = threshold });
    }
}
