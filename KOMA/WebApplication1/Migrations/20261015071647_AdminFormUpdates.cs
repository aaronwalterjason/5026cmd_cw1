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

            // The staff repair migration may already have added this column.
            migrationBuilder.Sql(@"
                IF COL_LENGTH('dbo.RestaurantTables', 'Shape') IS NULL
                BEGIN
                    ALTER TABLE [dbo].[RestaurantTables]
                    ADD [Shape] int NOT NULL DEFAULT (0);
                END
            ");

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

            // Preserve Floor values when the staff repair has already run.
            migrationBuilder.Sql(@"
                IF COL_LENGTH('dbo.DiningAreas', 'Floor') IS NULL
                BEGIN
                    ALTER TABLE [dbo].[DiningAreas]
                    ADD [Floor] nvarchar(30) NOT NULL DEFAULT (N'Ground Floor');
                END
            ");
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
