using ArbitrageApi.Hubs;
using ArbitrageApi.Models;
using ArbitrageApi.Services;
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
        policy.WithOrigins("http://localhost:5173", "http://localhost:3000")
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
    return new ArbitrageApi.Services.Exchanges.BinanceClient(httpClient, logger, configuration);
});

builder.Services.AddSingleton<ArbitrageApi.Services.Exchanges.CoinbaseClient>(sp =>
{
    var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
    var httpClient = httpClientFactory.CreateClient(nameof(ArbitrageApi.Services.Exchanges.CoinbaseClient));
    var logger = sp.GetRequiredService<ILogger<ArbitrageApi.Services.Exchanges.CoinbaseClient>>();
    var configuration = sp.GetRequiredService<IConfiguration>();
    return new ArbitrageApi.Services.Exchanges.CoinbaseClient(httpClient, logger, configuration);
});

// Register as IExchangeClient as well (using the same instances)
builder.Services.AddSingleton<ArbitrageApi.Services.Exchanges.IExchangeClient>(sp => sp.GetRequiredService<ArbitrageApi.Services.Exchanges.BinanceClient>());
builder.Services.AddSingleton<ArbitrageApi.Services.Exchanges.IExchangeClient>(sp => sp.GetRequiredService<ArbitrageApi.Services.Exchanges.CoinbaseClient>());

// Add HttpClient for the factory to work
builder.Services.AddHttpClient();

// Register Services
builder.Services.AddSingleton<StatePersistenceService>();
builder.Services.AddSingleton<TradeService>();
builder.Services.AddHostedService(provider => provider.GetRequiredService<TradeService>());
builder.Services.AddSingleton<ArbitrageStatsService>();
builder.Services.AddHostedService(provider => provider.GetRequiredService<ArbitrageStatsService>());

// Register background service as singleton so we can inject it into controller
builder.Services.AddSingleton<ArbitrageDetectionService>();
builder.Services.AddHostedService(provider => provider.GetRequiredService<ArbitrageDetectionService>());

var app = builder.Build();

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

// Enable CORS
app.UseCors("AllowFrontend");

app.UseAuthorization();

app.MapControllers();

// Map SignalR hub
app.MapHub<ArbitrageHub>("/arbitrageHub");

Console.WriteLine("🚀 Arbitrage API Server starting with REAL exchange data...");
Console.WriteLine("📡 SignalR Hub available at: /arbitrageHub");
Console.WriteLine("🌐 API endpoints available at: /api/arbitrage");
Console.WriteLine("💱 Exchanges: Binance, Coinbase");

app.Run();

