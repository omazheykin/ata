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
builder.Services.AddTransient<StatsBootstrapService>();
builder.Services.AddSingleton<ArbitrageExportService>();
builder.Services.AddTransient<IStatsAggregator, StatsAggregator>();

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
Console.WriteLine("💱 Exchanges: Binance, Coinbase");

app.Run();

