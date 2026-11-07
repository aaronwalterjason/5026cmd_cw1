using Microsoft.EntityFrameworkCore.Migrations;

namespace KOMA.Migrations;

public partial class PairTableTablet : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.AddColumn<Guid>(name: "TabletDeviceId", table: "RestaurantTables", type: "uniqueidentifier", nullable: true);

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropColumn(name: "TabletDeviceId", table: "RestaurantTables");
}
