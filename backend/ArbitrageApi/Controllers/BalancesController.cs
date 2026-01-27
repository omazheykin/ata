using ArbitrageApi.Models;
using ArbitrageApi.Services;
using Microsoft.AspNetCore.Mvc;

namespace ArbitrageApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class BalancesController : ControllerBase
{
    private readonly ILogger<BalancesController> _logger;
    private readonly IEnumerable<ArbitrageApi.Services.Exchanges.IExchangeClient> _exchangeClients;

    public BalancesController(IEnumerable<ArbitrageApi.Services.Exchanges.IExchangeClient> exchangeClients, ILogger<BalancesController> logger)
    {
        _logger = logger;
        _exchangeClients = exchangeClients;
    }

    [HttpPost("deposit")]
    public async Task<ActionResult> Deposit([FromBody] DepositRequest request)
    {
        _logger.LogInformation("📥 Received deposit request: {Amount} {Asset} to {Exchange}", request.Amount, request.Asset, request.Exchange);
        
        var client = _exchangeClients.FirstOrDefault(c => c.ExchangeName == request.Exchange);
        if (client == null) return NotFound("Exchange not found");

        await client.DepositSandboxFundsAsync(request.Asset, request.Amount);
        return Ok(new { Success = true, Message = $"Deposited {request.Amount} {request.Asset} to {request.Exchange}" });
    }

    [HttpGet("balances")]
    public async Task<ActionResult<Dictionary<string, List<Balance>>>> GetBalances()
    {
        try
        {
            var results = new Dictionary<string, List<Balance>>();
            foreach (var client in _exchangeClients)
            {
                var balances = await client.GetBalancesAsync();
                results[client.ExchangeName] = balances;
            }
            return Ok(results);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving balances");
            return StatusCode(500, "An error occurred while retrieving balances");
        }
    }

}

public record DepositRequest(string Exchange, string Asset, decimal Amount);
