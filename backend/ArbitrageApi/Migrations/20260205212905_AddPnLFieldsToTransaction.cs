using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArbitrageApi.Migrations
{
    /// <inheritdoc />
    public partial class AddPnLFieldsToTransaction : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "BuyCost",
                table: "Transactions",
                type: "decimal(18,8)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "RealizedProfit",
                table: "Transactions",
                type: "decimal(18,8)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "SellProceeds",
                table: "Transactions",
                type: "decimal(18,8)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "TotalFees",
                table: "Transactions",
                type: "decimal(18,8)",
                nullable: false,
                defaultValue: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BuyCost",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "RealizedProfit",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "SellProceeds",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "TotalFees",
                table: "Transactions");
        }
    }
}
