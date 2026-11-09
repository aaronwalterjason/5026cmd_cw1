using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KOMA.Migrations
{
    /// <inheritdoc />
    public partial class OpeningHoursPerDay : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ClosedDates",
                table: "RestaurantSettings",
                type: "nvarchar(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LastBookingMinutes",
                table: "RestaurantSettings",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "WeeklyHours",
                table: "RestaurantSettings",
                type: "nvarchar(120)",
                maxLength: 120,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ClosedDates",
                table: "RestaurantSettings");

            migrationBuilder.DropColumn(
                name: "LastBookingMinutes",
                table: "RestaurantSettings");

            migrationBuilder.DropColumn(
                name: "WeeklyHours",
                table: "RestaurantSettings");
        }
    }
}
