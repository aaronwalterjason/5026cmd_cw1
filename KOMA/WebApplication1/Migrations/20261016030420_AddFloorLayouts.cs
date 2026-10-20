using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KOMA.Migrations
{
    /// <inheritdoc />
    public partial class AddFloorLayouts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Floor",
                table: "DiningAreas");

            migrationBuilder.AddColumn<double>(
                name: "LayoutHeight",
                table: "RestaurantTables",
                type: "float",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<double>(
                name: "LayoutWidth",
                table: "RestaurantTables",
                type: "float",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<double>(
                name: "LayoutX",
                table: "RestaurantTables",
                type: "float",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<double>(
                name: "LayoutY",
                table: "RestaurantTables",
                type: "float",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<int>(
                name: "FloorLayoutId",
                table: "DiningAreas",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "LayoutHeight",
                table: "DiningAreas",
                type: "float",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<double>(
                name: "LayoutWidth",
                table: "DiningAreas",
                type: "float",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<double>(
                name: "LayoutX",
                table: "DiningAreas",
                type: "float",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<double>(
                name: "LayoutY",
                table: "DiningAreas",
                type: "float",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.CreateTable(
                name: "FloorLayouts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Level = table.Column<int>(type: "int", nullable: false),
                    Width = table.Column<int>(type: "int", nullable: false),
                    Height = table.Column<int>(type: "int", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FloorLayouts", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DiningAreas_FloorLayoutId",
                table: "DiningAreas",
                column: "FloorLayoutId");

            migrationBuilder.AddForeignKey(
                name: "FK_DiningAreas_FloorLayouts_FloorLayoutId",
                table: "DiningAreas",
                column: "FloorLayoutId",
                principalTable: "FloorLayouts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_DiningAreas_FloorLayouts_FloorLayoutId",
                table: "DiningAreas");

            migrationBuilder.DropTable(
                name: "FloorLayouts");

            migrationBuilder.DropIndex(
                name: "IX_DiningAreas_FloorLayoutId",
                table: "DiningAreas");

            migrationBuilder.DropColumn(
                name: "LayoutHeight",
                table: "RestaurantTables");

            migrationBuilder.DropColumn(
                name: "LayoutWidth",
                table: "RestaurantTables");

            migrationBuilder.DropColumn(
                name: "LayoutX",
                table: "RestaurantTables");

            migrationBuilder.DropColumn(
                name: "LayoutY",
                table: "RestaurantTables");

            migrationBuilder.DropColumn(
                name: "FloorLayoutId",
                table: "DiningAreas");

            migrationBuilder.DropColumn(
                name: "LayoutHeight",
                table: "DiningAreas");

            migrationBuilder.DropColumn(
                name: "LayoutWidth",
                table: "DiningAreas");

            migrationBuilder.DropColumn(
                name: "LayoutX",
                table: "DiningAreas");

            migrationBuilder.DropColumn(
                name: "LayoutY",
                table: "DiningAreas");

            migrationBuilder.AddColumn<string>(
                name: "Floor",
                table: "DiningAreas",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "");
        }
    }
}
