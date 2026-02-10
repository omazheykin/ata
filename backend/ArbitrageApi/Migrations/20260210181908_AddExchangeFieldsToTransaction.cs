using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArbitrageApi.Migrations
{
    /// <inheritdoc />
    public partial class AddExchangeFieldsToTransaction : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BuyExchange",
                table: "Transactions",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SellExchange",
                table: "Transactions",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BuyExchange",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "SellExchange",
                table: "Transactions");
        }
    }
}
