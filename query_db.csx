#!/usr/bin/env dotnet-script
#r "nuget: Microsoft.Data.Sqlite, 8.0.0"

using Microsoft.Data.Sqlite;

var dbPath = @"backend\ArbitrageApi\arbitrage_stats.db";

using var connection = new SqliteConnection($"Data Source={dbPath}");
connection.Open();

// Get total count
var countCmd = connection.CreateCommand();
countCmd.CommandText = "SELECT COUNT(*) FROM ArbitrageEvents";
var totalCount = (long)countCmd.ExecuteScalar();
Console.WriteLine($"Total events in database: {totalCount}");

// Get max profit
var maxCmd = connection.CreateCommand();
maxCmd.CommandText = "SELECT MAX(ProfitPercentage) FROM ArbitrageEvents";
var maxProfit = maxCmd.ExecuteScalar();
Console.WriteLine($"Max profit ever recorded: {maxProfit}%");

// Get average profit
var avgCmd = connection.CreateCommand();
avgCmd.CommandText = "SELECT AVG(ProfitPercentage) FROM ArbitrageEvents";
var avgProfit = avgCmd.ExecuteScalar();
Console.WriteLine($"Average profit: {avgProfit}%");

// Get recent top 10
var topCmd = connection.CreateCommand();
topCmd.CommandText = @"
    SELECT Symbol, ProfitPercentage, Timestamp 
    FROM ArbitrageEvents 
    ORDER BY ProfitPercentage DESC 
    LIMIT 10";

Console.WriteLine("\nTop 10 most profitable opportunities:");
using var reader = topCmd.ExecuteReader();
while (reader.Read())
{
    Console.WriteLine($"{reader.GetString(0)}: {reader.GetDouble(1):F4}% at {reader.GetDateTime(2)}");
}
