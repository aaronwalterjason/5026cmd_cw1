using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KOMA.Migrations
{
    /// <inheritdoc />
    public partial class AdminFormUpdates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DisplayOrder",
                table: "MenuCategories");

            migrationBuilder.AddColumn<int>(
                name: "Shape",
                table: "RestaurantTables",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Description",
                table: "MenuCategories",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Zone",
                table: "DiningAreas",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "Indoor",   // existing areas with no zone become Indoor
                oldClrType: typeof(string),
                oldType: "nvarchar(40)",
                oldMaxLength: 40,
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Floor",
                table: "DiningAreas",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "Ground Floor");   // existing areas start on Ground Floor
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Shape",
                table: "RestaurantTables");

            migrationBuilder.DropColumn(
                name: "Description",
                table: "MenuCategories");

            migrationBuilder.DropColumn(
                name: "Floor",
                table: "DiningAreas");

            migrationBuilder.AddColumn<int>(
                name: "DisplayOrder",
                table: "MenuCategories",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AlterColumn<string>(
                name: "Zone",
                table: "DiningAreas",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(40)",
                oldMaxLength: 40);
        }
    }
}
