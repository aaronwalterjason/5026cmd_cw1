using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KOMA.Migrations;

public partial class LinkReservationPreOrders : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(name: "ReservationId", table: "Orders", type: "int", nullable: true);
        migrationBuilder.CreateIndex(name: "IX_Orders_ReservationId", table: "Orders", column: "ReservationId",
            unique: true, filter: "[ReservationId] IS NOT NULL");
        migrationBuilder.AddForeignKey(name: "FK_Orders_Reservations_ReservationId", table: "Orders",
            column: "ReservationId", principalTable: "Reservations", principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(name: "FK_Orders_Reservations_ReservationId", table: "Orders");
        migrationBuilder.DropIndex(name: "IX_Orders_ReservationId", table: "Orders");
        migrationBuilder.DropColumn(name: "ReservationId", table: "Orders");
    }
}
