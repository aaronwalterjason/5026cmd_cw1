using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KOMA.Migrations
{
    /// <inheritdoc />
    public partial class AddTableServiceSessions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "TableServiceSessionId",
                table: "Orders",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "TableServiceSessions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RestaurantTableId = table.Column<int>(type: "int", nullable: false),
                    ReservationId = table.Column<int>(type: "int", nullable: true),
                    CustomerName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ContactNumber = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    GuestCount = table.Column<int>(type: "int", nullable: false),
                    StartTime = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EndTime = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TableServiceSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TableServiceSessions_Reservations_ReservationId",
                        column: x => x.ReservationId,
                        principalTable: "Reservations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TableServiceSessions_RestaurantTables_RestaurantTableId",
                        column: x => x.RestaurantTableId,
                        principalTable: "RestaurantTables",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Orders_TableServiceSessionId",
                table: "Orders",
                column: "TableServiceSessionId");

            migrationBuilder.CreateIndex(
                name: "IX_TableServiceSessions_ReservationId",
                table: "TableServiceSessions",
                column: "ReservationId",
                unique: true,
                filter: "[ReservationId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_TableServiceSessions_RestaurantTableId_StartTime_EndTime",
                table: "TableServiceSessions",
                columns: new[] { "RestaurantTableId", "StartTime", "EndTime" });

            migrationBuilder.AddForeignKey(
                name: "FK_Orders_TableServiceSessions_TableServiceSessionId",
                table: "Orders",
                column: "TableServiceSessionId",
                principalTable: "TableServiceSessions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Orders_TableServiceSessions_TableServiceSessionId",
                table: "Orders");

            migrationBuilder.DropTable(
                name: "TableServiceSessions");

            migrationBuilder.DropIndex(
                name: "IX_Orders_TableServiceSessionId",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "TableServiceSessionId",
                table: "Orders");
        }
    }
}
