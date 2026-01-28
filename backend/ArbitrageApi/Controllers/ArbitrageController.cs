using ArbitrageApi.Models;
using ArbitrageApi.Services;
using Microsoft.AspNetCore.Mvc;

namespace ArbitrageApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ArbitrageController : ControllerBase
{
    private readonly TradeService _tradeService;
    private readonly IEnumerable<ArbitrageApi.Services.Exchanges.IExchangeClient> _exchangeClients;
    private readonly ILogger<ArbitrageController> _logger;

    public ArbitrageController(
        TradeService tradeService,
        IEnumerable<ArbitrageApi.Services.Exchanges.IExchangeClient> exchangeClients,
        ILogger<ArbitrageController> logger)
    {
        _tradeService = tradeService;
        _exchangeClients = exchangeClients;
        _logger = logger;
    }

 
    [HttpGet("recent")]
    public ActionResult<List<ArbitrageOpportunity>> GetRecentOpportunities()
    {
        try
        {
            var opportunities = _tradeService.GetRecentOpportunities();
            return Ok(opportunities);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving recent opportunities");
            return StatusCode(500, "An error occurred while retrieving opportunities");
        }
    }

    [HttpGet("statistics")]
    public ActionResult<object> GetStatistics()
    {
        try
        {
            var opportunities = _tradeService.GetRecentOpportunities();

            var stats = new
            {
                TotalOpportunities = opportunities.Count,
                AverageProfitPercentage = opportunities.Any()
                    ? Math.Round(opportunities.Average(o => o.ProfitPercentage), 2)
                    : 0,
                BestOpportunity = opportunities
                    .OrderByDescending(o => o.ProfitPercentage)
                    .FirstOrDefault(),
                TotalVolume = opportunities.Sum(o => o.Volume),
                ActiveExchanges = opportunities
                    .SelectMany(o => new[] { o.BuyExchange, o.SellExchange })
                    .Distinct()
                    .Count()
            };

            return Ok(stats);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calculating statistics");
            return StatusCode(500, "An error occurred while calculating statistics");
        }
    }

    [HttpGet("exchanges")]
    public ActionResult<List<string>> GetExchanges()
    {
        return Ok(Exchange.All);
    }

}
