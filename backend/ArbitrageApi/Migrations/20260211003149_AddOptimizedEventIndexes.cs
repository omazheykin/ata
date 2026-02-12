using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArbitrageApi.Migrations
{
    /// <inheritdoc />
    public partial class AddOptimizedEventIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DayOfWeek",
                table: "ArbitrageEvents",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Hour",
                table: "ArbitrageEvents",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            // Populate existing rows
            migrationBuilder.Sql("UPDATE ArbitrageEvents SET DayOfWeek = STRFTIME('%w', Timestamp), Hour = STRFTIME('%H', Timestamp);");

            migrationBuilder.CreateIndex(
                name: "IX_ArbitrageEvents_DayOfWeek_Hour_Timestamp",
                table: "ArbitrageEvents",
                columns: new[] { "DayOfWeek", "Hour", "Timestamp" });

            migrationBuilder.CreateIndex(
                name: "IX_ArbitrageEvents_Pair",
                table: "ArbitrageEvents",
                column: "Pair");

            migrationBuilder.CreateIndex(
                name: "IX_ArbitrageEvents_Timestamp",
                table: "ArbitrageEvents",
                column: "Timestamp");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ArbitrageEvents_DayOfWeek_Hour_Timestamp",
                table: "ArbitrageEvents");

            migrationBuilder.DropIndex(
                name: "IX_ArbitrageEvents_Pair",
                table: "ArbitrageEvents");

            migrationBuilder.DropIndex(
                name: "IX_ArbitrageEvents_Timestamp",
                table: "ArbitrageEvents");

            migrationBuilder.DropColumn(
                name: "DayOfWeek",
                table: "ArbitrageEvents");

            migrationBuilder.DropColumn(
                name: "Hour",
                table: "ArbitrageEvents");
        }
    }
}
