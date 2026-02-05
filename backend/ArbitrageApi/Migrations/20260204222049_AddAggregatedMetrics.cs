using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArbitrageApi.Migrations
{
    /// <inheritdoc />
    public partial class AddAggregatedMetrics : Migration
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
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AggregatedMetrics");
        }
    }
}
