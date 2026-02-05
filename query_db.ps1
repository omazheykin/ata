Add-Type -Path "C:\Windows\Microsoft.NET\assembly\GAC_MSIL\System.Data.SQLite\v4.0_1.0.118.0__db937bc2d44ff139\System.Data.SQLite.dll" -ErrorAction SilentlyContinue

$dbPath = "backend\ArbitrageApi\arbitrage_stats.db"

try {
    $connection = New-Object System.Data.SQLite.SQLiteConnection("Data Source=$dbPath")
    $connection.Open()
    
    # Total count
    $cmd = $connection.CreateCommand()
    $cmd.CommandText = "SELECT COUNT(*) FROM ArbitrageEvents"
    $totalCount = $cmd.ExecuteScalar()
    Write-Host "Total events: $totalCount"
    
    # Max profit
    $cmd.CommandText = "SELECT MAX(ProfitPercentage) FROM ArbitrageEvents"
    $maxProfit = $cmd.ExecuteScalar()
    Write-Host "Max profit: $maxProfit%"
    
    # Average
    $cmd.CommandText = "SELECT AVG(ProfitPercentage) FROM ArbitrageEvents"
    $avgProfit = $cmd.ExecuteScalar()
    Write-Host "Average profit: $avgProfit%"
    
    # Top 10
    $cmd.CommandText = "SELECT Symbol, ProfitPercentage, Timestamp FROM ArbitrageEvents ORDER BY ProfitPercentage DESC LIMIT 10"
    $reader = $cmd.ExecuteReader()
    Write-Host "`nTop 10:"
    while ($reader.Read()) {
        Write-Host "$($reader[0]): $($reader[1])% at $($reader[2])"
    }
    
    $connection.Close()
} catch {
    Write-Host "Error: $_"
}
