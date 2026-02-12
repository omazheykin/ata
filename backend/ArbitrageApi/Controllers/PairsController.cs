using ArbitrageApi.Configuration;
using Microsoft.AspNetCore.Mvc;

namespace ArbitrageApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PairsController : ControllerBase
{
    private readonly PairsConfigRoot _config;

    public PairsController(PairsConfigRoot config)
    {
        _config = config;
    }

    [HttpGet]
    public ActionResult<IEnumerable<PairConfig>> GetTrackedPairs()
    {
        return Ok(_config.Pairs);
    }
}
