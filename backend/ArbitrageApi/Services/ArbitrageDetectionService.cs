using ArbitrageApi.Configuration;
using ArbitrageApi.Hubs;
using ArbitrageApi.Models;
using ArbitrageApi.Services.Exchanges;
using ArbitrageApi.Services.Stats;
using Microsoft.AspNetCore.SignalR;
using System.Threading.Channels;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System;

namespace ArbitrageApi.Services;

public class ArbitrageDetectionService : BackgroundService
{
    private readonly IEnumerable<IBookProvider> _bookProviders;
    private readonly IHubContext<ArbitrageHub> _hubContext;
    private readonly ILogger<ArbitrageDetectionService> _logger;
    private readonly ChannelWriter<ArbitrageOpportunity> _opportunities;
    private readonly ChannelWriter<CalendarEvent> _calendarStats;
    private readonly DepthThresholdService _depthThresholds;
    private readonly PairsConfigRoot _pairsConfig;
    private readonly ChannelProvider _channelProvider; // Kept for other channels if needed
    private readonly StatePersistenceService _persistenceService;
    private readonly ConcurrentQueue<ArbitrageOpportunity> _recentOpportunities = new();
    
    // We keep IsSandboxMode property for the controller/UI compatibility
    private bool _isSandboxMode = false;

    public bool IsSandboxMode => _isSandboxMode;
    public List<ArbitrageOpportunity> GetRecentOpportunities() => _recentOpportunities.Reverse().ToList();

    public ArbitrageDetectionService(
        IEnumerable<IBookProvider> bookProviders,
        IHubContext<ArbitrageHub> hubContext,
        ILogger<ArbitrageDetectionService> logger,
        ChannelProvider channels,
        DepthThresholdService depthThresholds,
        PairsConfigRoot pairsConfig,
        StatePersistenceService persistenceService)
    {
        _bookProviders = bookProviders;
        _hubContext = hubContext;
        _logger = logger;
        _channelProvider = channels;
        _opportunities = channels.TradeChannel.Writer; // We map this to TradeChannel for now, or create a new "RawOpportunities" channel? 
                                                       // The requirement says "Detector -> Opportunities (trade candidates)".
                                                       // Existing TradeService reads from TradeChannel.
        _calendarStats = channels.CalendarStats.Writer; // We need to add this property to ChannelProvider
        _depthThresholds = depthThresholds;
        _pairsConfig = pairsConfig;
        _persistenceService = persistenceService;
        
        var state = _persistenceService.GetState();
        _isSandboxMode = state.IsSandboxMode;
    }

    protected override async Task ExecuteAsync(CancellationToken token)
    {
        _logger.LogInformation("🚀 Pure Arbitrage Detector started.");

        while (!token.IsCancellationRequested)
        {
            var now = DateTime.UtcNow;

            // Iterate over all configured pairs
            foreach (var pairConfig in _pairsConfig.Pairs)
            {
                await DetectForPairAsync(pairConfig, now, token);
            }

            // Small delay to prevent CPU spinning, 
            // though in a real event-driven system we'd react to WS events.
            // For this implementation loop, 10ms is fine.
            await Task.Delay(10, token);
        }
    }

    private async Task DetectForPairAsync(PairConfig pair, DateTime now, CancellationToken token)
    {
        // 1. Get Top-of-Book from all providers
        // We need robust way to get "Binance" and "Coinbase" specifically if mapped, 
        // or just generic "all providers" if we are searching for ANY arb.
        // For simplicity/compliance with current requirement which implies specific pairs:
        
        var binance = _bookProviders.FirstOrDefault(p => p.ExchangeName == "Binance")?.GetOrderBook(pair.Symbol);
        var coinbase = _bookProviders.FirstOrDefault(p => p.ExchangeName == "Coinbase")?.GetOrderBook(pair.Symbol);

        if (binance == null || coinbase == null) return;
        
        // 2. Determine Threshold based on Time/Zone
        var threshold = _depthThresholds.GetDepthThreshold(pair.Symbol, now);

        // 3. Check Direction A: Binance -> Coinbase
        await DetectDirectionAsync(pair, now, 
            binance.Value.Asks.FirstOrDefault(), 
            coinbase.Value.Bids.FirstOrDefault(),
            "Binance", "Coinbase", threshold, token);

        // 4. Check Direction B: Coinbase -> Binance
        await DetectDirectionAsync(pair, now, 
            coinbase.Value.Asks.FirstOrDefault(), 
            binance.Value.Bids.FirstOrDefault(),
            "Coinbase", "Binance", threshold, token);
    }

    private async Task DetectDirectionAsync(
        PairConfig pair,
        DateTime now,
        (decimal Price, decimal Quantity) buySide,
        (decimal Price, decimal Quantity) sellSide,
        string buyEx,
        string sellEx,
        double threshold,
        CancellationToken token)
    {
        if (buySide.Price <= 0 || sellSide.Price <= 0) return;

        // Core Calculation
        var buyPrice = (double)buySide.Price;
        var sellPrice = (double)sellSide.Price;
        var buyQty = (double)buySide.Quantity;
        var sellQty = (double)sellSide.Quantity;

        var depth = Math.Min(buyQty, sellQty);
        var spread = (sellPrice - buyPrice) / buyPrice; // Gross spread

        // 1. Calendar Filter: Valid Market Window?
        // Spread > 0 AND Depth >= TechnicalMin (Configured per pair, default 0.01)
        bool isCalendarValid = spread > 0 && depth >= pair.TechnicalMinDepth; 

        if (isCalendarValid)
        {
             await _calendarStats.WriteAsync(new CalendarEvent
             {
                 Pair = pair.Symbol,
                 Spread = spread,
                 Depth = depth,
                 TimestampUtc = now
             }, token);
        }

        // 2. Trading Filter: Actionable Opportunity?
        // Depth >= Dynamic Threshold AND Spread > 0
        if (depth < threshold) return;
        if (spread <= 0) return;

        var opp = new ArbitrageOpportunity
        {
            Id = Guid.NewGuid(),
            Symbol = pair.Symbol,
            Asset = pair.Symbol, // Simplified
            BuyExchange = buyEx,
            SellExchange = sellEx,
            BuyPrice = (decimal)buyPrice,
            SellPrice = (decimal)sellPrice,
            BuyDepth = (decimal)buyQty, // Storing depth info
            SellDepth = (decimal)sellQty,
            Volume = (decimal)depth, // actionable volume
            ProfitPercentage = (decimal)(spread * 100), // Convert to % for compatibility
            GrossProfitPercentage = (decimal)(spread * 100),
            Timestamp = now,
            Status = "Candidate",
            IsSandbox = _isSandboxMode
        };

        // Emit to Opportunities Channel (Trade Candidates)
        // Note: TradeService parses this, checks fees/balances, and executes.
        await _opportunities.WriteAsync(opp, token);
        
        // Cache for UI
        _recentOpportunities.Enqueue(opp);
        while (_recentOpportunities.Count > 50) _recentOpportunities.TryDequeue(out _);
        
        // Also broadcast to frontend for "Live Opportunities" view?
        // The requirements say "Detector -> Opportunities", usually for Trading.
        // Frontend likely needs this too.
        await _hubContext.Clients.All.SendAsync("ReceiveOpportunity", opp, token);
    }
    
    // Support methods for UI toggles (compatibility)
    public async Task SetSandboxMode(bool enabled)
    {
        _isSandboxMode = enabled;
        var state = _persistenceService.GetState();
        state.IsSandboxMode = enabled;
        _persistenceService.SaveState(state);
        await _hubContext.Clients.All.SendAsync("ReceiveSandboxModeUpdate", enabled);
    }
}
