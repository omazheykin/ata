using ArbitrageApi.Hubs;
using ArbitrageApi.Models;
using ArbitrageApi.Services;
using ArbitrageApi.Data;
using ArbitrageApi.Services.Stats;
using ArbitrageApi.Services.Strategies;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddOpenApi();

// Add SQLite DbContext
builder.Services.AddDbContext<ArbitrageApi.Data.StatsDbContext>(options =>
    options.UseSqlite("Data Source=arbitrage_stats.db"));

// Add SignalR
builder.Services.AddSignalR(options =>
{
    options.EnableDetailedErrors = true;
    options.KeepAliveInterval = TimeSpan.FromSeconds(15);
    options.ClientTimeoutInterval = TimeSpan.FromSeconds(30);
});

// Add CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        policy.WithOrigins(
                  "http://localhost:5173", 
                  "http://127.0.0.1:5173", 
                  "http://[::1]:5173",
                  "http://localhost:3000"
              )
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

// Register Exchange Clients as Singletons
builder.Services.AddSingleton<ArbitrageApi.Services.Exchanges.BinanceClient>(sp =>
{
    var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
    var httpClient = httpClientFactory.CreateClient(nameof(ArbitrageApi.Services.Exchanges.BinanceClient));
    var logger = sp.GetRequiredService<ILogger<ArbitrageApi.Services.Exchanges.BinanceClient>>();
    var configuration = sp.GetRequiredService<IConfiguration>();
    var statePersistence = sp.GetRequiredService<ArbitrageApi.Services.StatePersistenceService>();
    var isSandboxMode = statePersistence.GetState().IsSandboxMode;
    return new ArbitrageApi.Services.Exchanges.BinanceClient(httpClient, logger, configuration, isSandboxMode);
});

builder.Services.AddSingleton<ArbitrageApi.Services.Exchanges.CoinbaseClient>(sp =>
{
    var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
    var httpClient = httpClientFactory.CreateClient(nameof(ArbitrageApi.Services.Exchanges.CoinbaseClient));
    var logger = sp.GetRequiredService<ILogger<ArbitrageApi.Services.Exchanges.CoinbaseClient>>();
    var configuration = sp.GetRequiredService<IConfiguration>();
    var statePersistence = sp.GetRequiredService<ArbitrageApi.Services.StatePersistenceService>();
    var isSandboxMode = statePersistence.GetState().IsSandboxMode;
    return new ArbitrageApi.Services.Exchanges.CoinbaseClient(httpClient, logger, configuration, isSandboxMode);
});

// Register as IExchangeClient as well (using the same instances)
builder.Services.AddSingleton<ArbitrageApi.Services.Exchanges.IExchangeClient>(sp => sp.GetRequiredService<ArbitrageApi.Services.Exchanges.BinanceClient>());
builder.Services.AddSingleton<ArbitrageApi.Services.Exchanges.IExchangeClient>(sp => sp.GetRequiredService<ArbitrageApi.Services.Exchanges.CoinbaseClient>());

// Register OKX Client
builder.Services.AddSingleton<ArbitrageApi.Services.Exchanges.OKX.OKXClient>(sp =>
{
    var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
    var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
    var configuration = sp.GetRequiredService<IConfiguration>();
    var statePersistence = sp.GetRequiredService<StatePersistenceService>();
    var isSandboxMode = statePersistence.GetState().IsSandboxMode;
    return new ArbitrageApi.Services.Exchanges.OKX.OKXClient(configuration, httpClientFactory, loggerFactory, isSandboxMode);
});
builder.Services.AddSingleton<ArbitrageApi.Services.Exchanges.IExchangeClient>(sp => sp.GetRequiredService<ArbitrageApi.Services.Exchanges.OKX.OKXClient>());

// Add HttpClient for the factory to work
builder.Services.AddHttpClient();

// Register Arbitrage Calculator
builder.Services.AddSingleton<ArbitrageCalculator>();

// Register WebSocket Clients and IBookProviders
builder.Services.AddSingleton<ArbitrageApi.Services.Exchanges.Binance.BinanceWsClient>();
builder.Services.AddSingleton<ArbitrageApi.Services.Exchanges.IBookProvider>(sp => sp.GetRequiredService<ArbitrageApi.Services.Exchanges.Binance.BinanceWsClient>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<ArbitrageApi.Services.Exchanges.Binance.BinanceWsClient>());

// builder.Services.AddSingleton<ArbitrageApi.Services.Exchanges.Coinbase.CoinbaseWsClient>();
// builder.Services.AddSingleton<ArbitrageApi.Services.Exchanges.IBookProvider>(sp => sp.GetRequiredService<ArbitrageApi.Services.Exchanges.Coinbase.CoinbaseWsClient>());
// builder.Services.AddHostedService(sp => sp.GetRequiredService<ArbitrageApi.Services.Exchanges.Coinbase.CoinbaseWsClient>());

builder.Services.AddSingleton<ArbitrageApi.Services.Exchanges.Coinbase.CoinbaseHttpBookProvider>();
builder.Services.AddSingleton<ArbitrageApi.Services.Exchanges.IBookProvider>(sp => sp.GetRequiredService<ArbitrageApi.Services.Exchanges.Coinbase.CoinbaseHttpBookProvider>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<ArbitrageApi.Services.Exchanges.Coinbase.CoinbaseHttpBookProvider>());

// Register OKX WebSocket Client
builder.Services.AddSingleton<ArbitrageApi.Services.Exchanges.OKX.OKXWsClient>();
builder.Services.AddSingleton<ArbitrageApi.Services.Exchanges.IBookProvider>(sp => sp.GetRequiredService<ArbitrageApi.Services.Exchanges.OKX.OKXWsClient>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<ArbitrageApi.Services.Exchanges.OKX.OKXWsClient>());

// Register Configuration & Stats Services
builder.Services.AddSingleton<ArbitrageApi.Configuration.PairsConfigRoot>(sp =>
{
    var config = new ArbitrageApi.Configuration.PairsConfigRoot();
    var defaultThresholds = new Dictionary<string, (double Min, double Opt, double Agg)>
    {
        { "BTCUSDT", (0.05, 0.5, 0.01) },
        { "ETHUSDT", (0.5, 5.0, 0.1) },
        { "SOLUSDT", (5.0, 50.0, 1.0) },
        { "XRPUSDT", (500.0, 5000.0, 100.0) },
        { "ADAUSDT", (500.0, 5000.0, 100.0) },
        { "DOGEUSDT", (1000.0, 10000.0, 200.0) },
        { "LTCUSDT", (2.0, 20.0, 0.5) },
        { "LINKUSDT", (10.0, 100.0, 2.0) },
        { "BCHUSDT", (1.0, 10.0, 0.2) },
        { "XLMUSDT", (500.0, 5000.0, 100.0) },
        { "SUSDT", (1000.0, 10000.0, 200.0) }
    };

    config.Pairs = TradingPair.CommonPairs.Select(p => 
    {
        var symbol = p.Symbol;
        var thresholds = defaultThresholds.GetValueOrDefault(symbol, (0.5, 1.0, 0.1)); // Generic default for new pairs
        return new ArbitrageApi.Configuration.PairConfig
        {
            Symbol = symbol,
            MinDepth = thresholds.Item1,
            OptimalDepth = thresholds.Item2,
            AggressiveDepth = thresholds.Item3
        };
    }).ToList();

    return config;
});

builder.Services.AddSingleton<CalendarCache>();
builder.Services.AddSingleton<DepthThresholdService>();

// Register Services
builder.Services.AddSingleton<ChannelProvider>();
builder.Services.AddSingleton<StatePersistenceService>();
builder.Services.AddSingleton<ITrendAnalysisService, TrendAnalysisService>();
builder.Services.AddSingleton<RebalancingService>();
builder.Services.AddSingleton<IRebalancingService>(sp => sp.GetRequiredService<RebalancingService>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<RebalancingService>());
builder.Services.AddSingleton<OrderExecutionService>();
builder.Services.AddSingleton<PassiveRebalancingService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<PassiveRebalancingService>());
builder.Services.AddSingleton<TradeService>();
builder.Services.AddHostedService(provider => provider.GetRequiredService<TradeService>());
builder.Services.AddSingleton<ArbitrageStatsService>();
builder.Services.AddHostedService(provider => provider.GetRequiredService<ArbitrageStatsService>());
builder.Services.AddSingleton<SmartStrategyService>();
builder.Services.AddHostedService(provider => provider.GetRequiredService<SmartStrategyService>());
// Register Stats & Calendar Services
builder.Services.AddTransient<StatsBootstrapService>();
builder.Services.AddSingleton<ArbitrageExportService>();
// Register Stats Aggregators
builder.Services.AddTransient<IStatsAggregator, HourAggregator>();
builder.Services.AddTransient<IStatsAggregator, DayAggregator>();
builder.Services.AddTransient<IStatsAggregator, PairAggregator>();
builder.Services.AddTransient<IStatsAggregator, GlobalAggregator>();
builder.Services.AddTransient<IStatsAggregator, DirectionAggregator>();

// The CompositeStatsAggregator will collect all IStatsAggregators and run them.
// It is registered last to be the primary implementation.
builder.Services.AddTransient<IStatsAggregator>(sp => 
{
    var aggregators = sp.GetServices<IStatsAggregator>();
    return new CompositeStatsAggregator(aggregators);
});
builder.Services.AddScoped<HistoricalAnalysisService>();
builder.Services.AddHostedService<ArbitrageApi.Services.Stats.CalendarStatsService>();

// Register Event Processors for Stats Chain
builder.Services.AddTransient<ArbitrageApi.Services.Stats.Processors.NormalizationProcessor>();
builder.Services.AddTransient<ArbitrageApi.Services.Stats.Processors.PersistenceProcessor>();
builder.Services.AddTransient<ArbitrageApi.Services.Stats.Processors.HeatmapProcessor>();
builder.Services.AddTransient<ArbitrageApi.Services.Stats.Processors.SummaryProcessor>();

// Register background service as singleton so we can inject it into controller
builder.Services.AddSingleton<ArbitrageDetectionService>();
builder.Services.AddHostedService(provider => provider.GetRequiredService<ArbitrageDetectionService>());

builder.Services.AddSingleton<SafetyMonitoringService>();
builder.Services.AddHostedService(provider => provider.GetRequiredService<SafetyMonitoringService>());

var app = builder.Build();

// Database Initialization (Critical Path)
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    var logger = services.GetRequiredService<ILogger<Program>>();
    try
    {
        var dbContext = services.GetRequiredService<StatsDbContext>();
        var bootstrapService = services.GetRequiredService<StatsBootstrapService>();

        logger.LogInformation("🔄 Initializing database...");
        await dbContext.Database.MigrateAsync();
        logger.LogInformation("✅ Database schema is up to date.");

        // Check if we need to bootstrap AggregatedMetrics or HeatmapCells
        if (!await dbContext.AggregatedMetrics.AnyAsync() || !await dbContext.HeatmapCells.AnyAsync())
        {
            logger.LogInformation("📊 Database tables empty. Starting bootstrap process...");
            await bootstrapService.BootstrapAggregationAsync(dbContext, CancellationToken.None);
        }
        else
        {
            logger.LogInformation("📊 Statistics already initialized. Skipping bootstrap.");
        }
    }
    catch (Exception ex)
    {
        logger.LogCritical(ex, "❌ Fatal error during database initialization. Application cannot start safely.");
        throw;
    }
}

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

// Enable CORS
app.UseCors("AllowFrontend");

app.UseAuthorization();

app.MapControllers();

// Quick stats endpoint
app.MapGet("/api/db-stats", async (StatsDbContext db) =>
{
    var totalCount = await db.ArbitrageEvents.CountAsync();
    if (totalCount == 0) return Results.Ok(new { message = "No events in database" });
    
    var maxProfit = await db.ArbitrageEvents.MaxAsync(e => e.Spread);
    var avgProfit = await db.ArbitrageEvents.AverageAsync(e => e.Spread);
    var minProfit = await db.ArbitrageEvents.MinAsync(e => e.Spread);
    
    var top10 = await db.ArbitrageEvents
        .OrderByDescending(e => e.Spread)
        .Take(10)
        .Select(e => new { e.Pair, Spread = e.Spread, e.Timestamp, e.Direction })
        .ToListAsync();
    
    return Results.Ok(new
    {
        totalEvents = totalCount,
        maxSpread = maxProfit,
        avgSpread = avgProfit,
        minSpread = minProfit,
        top10
    });
});

// Map SignalR hub
app.MapHub<ArbitrageHub>("/arbitrageHub");

Console.WriteLine("🚀 Arbitrage API Server starting with REAL exchange data...");
Console.WriteLine("📡 SignalR Hub available at: /arbitrageHub");
Console.WriteLine("🌐 API endpoints available at: /api/arbitrage");

app.Run();

