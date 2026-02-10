using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArbitrageApi.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AggregatedMetrics",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    Category = table.Column<string>(type: "TEXT", nullable: false),
                    MetricKey = table.Column<string>(type: "TEXT", nullable: false),
                    EventCount = table.Column<int>(type: "INTEGER", nullable: false),
                    SumSpread = table.Column<decimal>(type: "decimal(18,8)", nullable: false),
                    MaxSpread = table.Column<decimal>(type: "decimal(18,8)", nullable: false),
                    SumDepth = table.Column<decimal>(type: "decimal(18,8)", nullable: false),
                    LastUpdated = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AggregatedMetrics", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ArbitrageEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Pair = table.Column<string>(type: "TEXT", nullable: false),
                    Direction = table.Column<string>(type: "TEXT", nullable: false),
                    Spread = table.Column<decimal>(type: "decimal(18,8)", nullable: false),
                    DepthBuy = table.Column<decimal>(type: "decimal(18,8)", nullable: false),
                    DepthSell = table.Column<decimal>(type: "decimal(18,8)", nullable: false),
                    Timestamp = table.Column<DateTime>(type: "TEXT", nullable: false),
                    SpreadPercent = table.Column<decimal>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ArbitrageEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "HeatmapCells",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    Day = table.Column<string>(type: "TEXT", nullable: false),
                    Hour = table.Column<int>(type: "INTEGER", nullable: false),
                    EventCount = table.Column<int>(type: "INTEGER", nullable: false),
                    AvgSpread = table.Column<decimal>(type: "TEXT", nullable: false),
                    MaxSpread = table.Column<decimal>(type: "TEXT", nullable: false),
                    DirectionBias = table.Column<string>(type: "TEXT", nullable: false),
                    VolatilityScore = table.Column<decimal>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HeatmapCells", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Transactions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Timestamp = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Type = table.Column<string>(type: "TEXT", nullable: false),
                    Asset = table.Column<string>(type: "TEXT", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,8)", nullable: false),
                    Exchange = table.Column<string>(type: "TEXT", nullable: false),
                    Price = table.Column<decimal>(type: "decimal(18,8)", nullable: false),
                    Fee = table.Column<decimal>(type: "decimal(18,8)", nullable: false),
                    Profit = table.Column<decimal>(type: "decimal(18,8)", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    RealizedProfit = table.Column<decimal>(type: "decimal(18,8)", nullable: false),
                    TotalFees = table.Column<decimal>(type: "decimal(18,8)", nullable: false),
                    BuyCost = table.Column<decimal>(type: "decimal(18,8)", nullable: false),
                    SellProceeds = table.Column<decimal>(type: "decimal(18,8)", nullable: false),
                    BuyOrderId = table.Column<string>(type: "TEXT", nullable: true),
                    SellOrderId = table.Column<string>(type: "TEXT", nullable: true),
                    BuyOrderStatus = table.Column<string>(type: "TEXT", nullable: true),
                    SellOrderStatus = table.Column<string>(type: "TEXT", nullable: true),
                    Strategy = table.Column<int>(type: "INTEGER", nullable: false),
                    IsRecovered = table.Column<bool>(type: "INTEGER", nullable: false),
                    RecoveryOrderId = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Transactions", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AggregatedMetrics");

            migrationBuilder.DropTable(
                name: "ArbitrageEvents");

            migrationBuilder.DropTable(
                name: "HeatmapCells");

            migrationBuilder.DropTable(
                name: "Transactions");
        }
    }
}
