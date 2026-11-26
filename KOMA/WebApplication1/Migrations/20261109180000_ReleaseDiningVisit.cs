using Microsoft.EntityFrameworkCore.Migrations;

namespace KOMA.Migrations;

public partial class ReleaseDiningVisit : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.AddColumn<bool>("IsReleased", "TableServiceSessions", type: "bit", nullable: false, defaultValue: false);

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropColumn("IsReleased", "TableServiceSessions");
}