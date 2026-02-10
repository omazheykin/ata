using ArbitrageApi.Services;
using ArbitrageApi.Hubs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;

namespace ArbitrageApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SettingsController : ControllerBase
{
    private readonly ArbitrageDetectionService _detectionService;
    private readonly TradeService _tradeService;
    private readonly SafetyMonitoringService _safetyService;
    private readonly OrderExecutionService _executionService;
    private readonly StatePersistenceService _persistenceService;
    private readonly IHubContext<ArbitrageHub> _hubContext;

    public SettingsController(
        ArbitrageDetectionService detectionService, 
        TradeService tradeService,
        SafetyMonitoringService safetyService,
        OrderExecutionService executionService,
        StatePersistenceService persistenceService,
        IHubContext<ArbitrageHub> hubContext)
    {
        _detectionService = detectionService;
        _tradeService = tradeService;
        _safetyService = safetyService;
        _executionService = executionService;
        _persistenceService = persistenceService;
        _hubContext = hubContext;
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
        var state = _persistenceService.GetState();
        return Ok(new { Enabled = state.IsSmartStrategyEnabled });
    }

    [HttpPost("smart-strategy")]
    public async Task<ActionResult> SetSmartStrategy([FromQuery] bool enabled)
    {
        var state = _persistenceService.GetState();
        state.IsSmartStrategyEnabled = enabled;
        _persistenceService.SaveState(state);
        await _hubContext.Clients.All.SendAsync("ReceiveSmartStrategyUpdate", enabled);
        return Ok(new { Enabled = enabled });
    }

    [HttpGet("strategy-status")]
    public ActionResult GetStrategyStatus()
    {
        // This was previously computed by DetectionService. 
        // For now returning current threshold from state or similar.
        var state = _persistenceService.GetState();
        return Ok(new { Threshold = state.MinProfitThreshold, Reason = "Current Config" });
    }

    [HttpGet("state")]
    public ActionResult GetState()
    {
        return Ok(_persistenceService.GetState());
    }

    [HttpPost("pair-thresholds")]
    public async Task<ActionResult> SetPairThresholds([FromBody] Dictionary<string, decimal> thresholds)
    {
        var state = _persistenceService.GetState();
        state.PairThresholds = thresholds;
        _persistenceService.SaveState(state);
        await _hubContext.Clients.All.SendAsync("ReceivePairThresholdsUpdate", thresholds);
        return Ok(thresholds);
    }

    [HttpPost("safe-multiplier")]
    public async Task<ActionResult> SetSafeBalanceMultiplier([FromQuery] decimal multiplier)
    {
        var state = _persistenceService.GetState();
        state.SafeBalanceMultiplier = multiplier;
        _persistenceService.SaveState(state);
        await _hubContext.Clients.All.SendAsync("ReceiveSafeMultiplierUpdate", multiplier);
        return Ok(new { Multiplier = multiplier });
    }

    [HttpPost("taker-fees")]
    public async Task<ActionResult> SetUseTakerFees([FromQuery] bool enabled)
    {
        var state = _persistenceService.GetState();
        state.UseTakerFees = enabled;
        _persistenceService.SaveState(state);
        await _hubContext.Clients.All.SendAsync("ReceiveTakerFeesUpdate", enabled);
        return Ok(new { Enabled = enabled });
    }

    [HttpPost("auto-rebalance")]
    public async Task<ActionResult> SetAutoRebalance([FromQuery] bool enabled)
    {
        var state = _persistenceService.GetState();
        state.IsAutoRebalanceEnabled = enabled;
        _persistenceService.SaveState(state);
        await _hubContext.Clients.All.SendAsync("ReceiveAutoRebalanceUpdate", enabled);
        return Ok(new { Enabled = enabled });
    }

    [HttpPost("safety-reset")]
    public async Task<ActionResult> ResetSafetyKillSwitch()
    {
        var state = _persistenceService.GetState();
        state.IsSafetyKillSwitchTriggered = false;
        state.GlobalKillSwitchReason = string.Empty;
        state.IsAutoTradeEnabled = true; // Turn back on after reset
        _persistenceService.SaveState(state);
        
        await _hubContext.Clients.All.SendAsync("ReceiveSafetyUpdate", new { isTriggered = false });
        await _hubContext.Clients.All.SendAsync("ReceiveAutoTradeUpdate", true);
        
        return Ok(new { Success = true });
    }

    [HttpPost("safety-limits")]
    public async Task<ActionResult> SetSafetyLimits([FromQuery] decimal drawdown, [FromQuery] int losses)
    {
        var state = _persistenceService.GetState();
        state.MaxDrawdownUsd = drawdown;
        state.MaxConsecutiveLosses = losses;
        _persistenceService.SaveState(state);
        return Ok(new { Drawdown = drawdown, Losses = losses });
    }

    [HttpPost("rebalance-threshold")]
    public async Task<ActionResult> SetRebalanceThreshold([FromQuery] decimal threshold)
    {
        var state = _persistenceService.GetState();
        state.MinRebalanceSkewThreshold = threshold;
        _persistenceService.SaveState(state);
        return Ok(new { Threshold = threshold });
    }

    [HttpPost("wallet-override")]
    public async Task<ActionResult> SetWalletOverride([FromQuery] string asset, [FromQuery] string exchange, [FromQuery] string address)
    {
        var state = _persistenceService.GetState();
        if (!state.WalletOverrides.ContainsKey(asset))
        {
            state.WalletOverrides[asset] = new Dictionary<string, string>();
        }
        state.WalletOverrides[asset][exchange] = address;
        _persistenceService.SaveState(state);
        return Ok(new { Asset = asset, Exchange = exchange, Address = address });
    }

    [HttpPost("wallet-overrides")]
    public async Task<ActionResult> SetWalletOverrides([FromBody] Dictionary<string, Dictionary<string, string>> overrides)
    {
        var state = _persistenceService.GetState();
        state.WalletOverrides = overrides;
        _persistenceService.SaveState(state);
        return Ok(overrides);
    }
}
