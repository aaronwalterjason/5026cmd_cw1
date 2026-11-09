using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KOMA.Migrations
{
    /// <inheritdoc />
    public partial class PromotionPricing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "ListPrice",
                table: "OrderItems",
                type: "decimal(8,2)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PromotionTitle",
                table: "OrderItems",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ListPrice",
                table: "OrderItems");

            migrationBuilder.DropColumn(
                name: "PromotionTitle",
                table: "OrderItems");
        }
    }
}
