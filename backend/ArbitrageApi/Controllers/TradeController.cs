using ArbitrageApi.Hubs;
using ArbitrageApi.Models;
using ArbitrageApi.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;

namespace ArbitrageApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class TradeController : ControllerBase
{
    private readonly TradeService _tradeService;
    private readonly OrderExecutionService _executionService;
    private readonly IHubContext<ArbitrageHub> _hubContext;
    private readonly ILogger<TradeController> _logger;

    public TradeController(TradeService tradeService, OrderExecutionService executionService, IHubContext<ArbitrageHub> hubContext, ILogger<TradeController> logger)
    {
        _tradeService = tradeService;
        _executionService = executionService;
        _hubContext = hubContext;
        _logger = logger;
    }

    [HttpGet("transactions")]
    public ActionResult<List<Transaction>> GetTransactions()
    {
        return Ok(_executionService.GetRecentTransactions());
    }

    [HttpPost("autotrade")]
    public ActionResult SetAutoTrade([FromQuery] bool enabled)
    {
        _tradeService.SetAutoTrade(enabled);
        return Ok(new { Enabled = enabled });
    }

    [HttpPost("autotrade/threshold")]
    public ActionResult SetThreshold([FromQuery] decimal threshold)
    {
        _tradeService.SetMinProfitThreshold(threshold);
        return Ok(new { Threshold = threshold });
    }

    [HttpGet("autotrade/status")]
    public ActionResult GetAutoTradeStatus()
    {
        return Ok(new
        {
            Enabled = _tradeService.IsAutoTradeEnabled,
            Threshold = _tradeService.MinProfitThreshold
        });
    }

    [HttpGet("strategy")]
    public ActionResult GetStrategy()
    {
        return Ok(new { Strategy = _executionService.GetExecutionStrategy().ToString() });
    }

    [HttpPost("strategy")]
    public ActionResult SetStrategy([FromQuery] ExecutionStrategy strategy)
    {
        _executionService.SetExecutionStrategy(strategy);
        return Ok(new { Strategy = strategy.ToString() });
    }

    [HttpPost("execute")]
    public async Task<ActionResult> ExecuteTrade([FromBody] ArbitrageOpportunity opportunity)
    {
        // Manual execution ignores thresholds (pass 0 or 0.01)
        var success = await _executionService.ExecuteTradeAsync(opportunity, 0m);
        if (success)
        {
            // Notify clients about the new transaction
            await _hubContext.Clients.All.SendAsync("ReceiveTransaction", _executionService.GetRecentTransactions().First());
        }
        return Ok(new { Success = success });
    }
}
