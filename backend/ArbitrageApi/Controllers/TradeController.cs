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
    private readonly IHubContext<ArbitrageHub> _hubContext;
    private readonly ILogger<TradeController> _logger;

    public TradeController(TradeService tradeService, IHubContext<ArbitrageHub> hubContext, ILogger<TradeController> logger)
    {
        _tradeService = tradeService;
        _hubContext = hubContext;
        _logger = logger;
    }

    [HttpGet("transactions")]
    public ActionResult<List<Transaction>> GetTransactions()
    {
        return Ok(_tradeService.GetRecentTransactions());
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
        return Ok(new { Strategy = _tradeService.Strategy.ToString() });
    }

    [HttpPost("strategy")]
    public ActionResult SetStrategy([FromQuery] ExecutionStrategy strategy)
    {
        _tradeService.SetExecutionStrategy(strategy);
        return Ok(new { Strategy = strategy.ToString() });
    }

    [HttpPost("execute")]
    public async Task<ActionResult> ExecuteTrade([FromBody] ArbitrageOpportunity opportunity)
    {
        var success = await _tradeService.ExecuteTradeAsync(opportunity);
        if (success)
        {
            // Notify clients about the new transaction
            await _hubContext.Clients.All.SendAsync("ReceiveTransaction", _tradeService.GetRecentTransactions().First());
        }
        return Ok(new { Success = success });
    }
}
