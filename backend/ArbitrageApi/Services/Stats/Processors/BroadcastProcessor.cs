using ArbitrageApi.Data;
using ArbitrageApi.Models;
using Microsoft.AspNetCore.SignalR;
using ArbitrageApi.Hubs;

namespace ArbitrageApi.Services.Stats.Processors;

/// <summary>
/// Example implementation of IEventProcessor for the stats processing chain.
/// This demonstrates the Chain of Responsibility pattern used in ArbitrageStatsService.
/// 
/// ARCHITECTURE NOTES FOR DEV TEAM:
/// ================================
/// The stats system processes each ArbitrageEvent through a chain of processors:
/// 1. NormalizationProcessor - Converts spread to percentage format
/// 2. PersistenceProcessor   - Saves raw event to database
/// 3. HeatmapProcessor        - Updates heatmap aggregation table
/// 4. SummaryProcessor        - Updates global aggregated metrics (Phase 6)
/// 5. BroadcastProcessor      - (THIS CLASS) Real-time SignalR broadcast
/// 
/// Each processor implements IEventProcessor and receives:
/// - arbitrageEvent: The opportunity being processed
/// - dbContext: Database context for persistence operations
/// 
/// CURRENT STATUS: DISABLED
/// ========================
/// This processor is intentionally disabled because:
/// - The frontend has no handler for "ReceiveArbitrageEvent"
/// - ArbitrageDetectionService already broadcasts valuable opportunities via "ReceiveOpportunity"
/// - Broadcasting hundreds of events/second to all clients is wasteful
/// - Kept as reference implementation for the team
/// 
/// TO RE-ENABLE:
/// =============
/// 1. Uncomment the SendAsync line below
/// 2. Add frontend handler in signalRService.ts:
///    connection.on("ReceiveArbitrageEvent", (event) => { ... })
/// 3. Consider throttling to avoid overwhelming clients
/// </summary>
public class BroadcastProcessor : IEventProcessor
{
    private readonly IHubContext<ArbitrageHub> _hubContext;

    public BroadcastProcessor(IHubContext<ArbitrageHub> hubContext)
    {
        _hubContext = hubContext;
    }

    public async Task ProcessAsync(ArbitrageEvent arbitrageEvent, StatsDbContext dbContext)
    {
        // DISABLED: This broadcast is commented out because the frontend doesn't have a handler.
        // We already broadcast filtered opportunities via ArbitrageDetectionService.
        // 
        // If you want to enable this for debugging or monitoring purposes:
        // 1. Uncomment the line below
        // 2. Add a SignalR handler in the frontend (see class documentation above)
        // 3. Consider adding throttling to avoid performance issues
        
        // await _hubContext.Clients.All.SendAsync("ReceiveArbitrageEvent", arbitrageEvent);
        
        await Task.CompletedTask;
    }
}
