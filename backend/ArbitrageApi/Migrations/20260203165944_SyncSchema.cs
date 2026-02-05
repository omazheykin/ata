using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArbitrageApi.Migrations
{
    /// <inheritdoc />
    public partial class SyncSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Table already exists, just add the new column
            migrationBuilder.AddColumn<decimal>(
                name: "SpreadPercent",
                table: "ArbitrageEvents",
                type: "TEXT",
                nullable: false,
                defaultValue: 0m);

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

            // Transactions table already exists, so we skip creating it
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HeatmapCells");

            migrationBuilder.DropColumn(
                name: "SpreadPercent",
                table: "ArbitrageEvents");

            // No need to drop Transactions as we didn't create it in Up
        }
    }
}
