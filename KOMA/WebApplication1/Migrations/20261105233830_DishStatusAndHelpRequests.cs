using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KOMA.Migrations
{
    /// <inheritdoc />
    public partial class DishStatusAndHelpRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ReadyAt",
                table: "OrderItems",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ServedAt",
                table: "OrderItems",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ServedVia",
                table: "OrderItems",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Status",
                table: "OrderItems",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "HelpRequests",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RestaurantTableId = table.Column<int>(type: "int", nullable: true),
                    Type = table.Column<int>(type: "int", nullable: false),
                    Note = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    TakenBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    TakenAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DoneBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    DoneAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HelpRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HelpRequests_RestaurantTables_RestaurantTableId",
                        column: x => x.RestaurantTableId,
                        principalTable: "RestaurantTables",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_HelpRequests_DoneAt",
                table: "HelpRequests",
                column: "DoneAt");

            migrationBuilder.CreateIndex(
                name: "IX_HelpRequests_RestaurantTableId",
                table: "HelpRequests",
                column: "RestaurantTableId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HelpRequests");

            migrationBuilder.DropColumn(
                name: "ReadyAt",
                table: "OrderItems");

            migrationBuilder.DropColumn(
                name: "ServedAt",
                table: "OrderItems");

            migrationBuilder.DropColumn(
                name: "ServedVia",
                table: "OrderItems");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "OrderItems");
        }
    }
}
