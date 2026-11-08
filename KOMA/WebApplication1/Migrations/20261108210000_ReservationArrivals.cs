using Microsoft.EntityFrameworkCore.Migrations;

namespace KOMA.Migrations;

public partial class ReservationArrivals : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTime>("ContactAttemptAt", "Reservations", nullable: true);
        migrationBuilder.AddColumn<DateTime>("ExpectedArrival", "Reservations", nullable: true);
        migrationBuilder.AddColumn<DateTime>("HoldUntil", "Reservations", nullable: true);
        migrationBuilder.Sql("""
            UPDATE Reservations SET HoldUntil = DATEADD(minute, 30, StartTime)
            WHERE Status IN (0,1) AND EndTime > DATEADD(hour,8,SYSUTCDATETIME());
            """);
    }
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn("HoldUntil", "Reservations");
        migrationBuilder.DropColumn("ExpectedArrival", "Reservations");
        migrationBuilder.DropColumn("ContactAttemptAt", "Reservations");
    }
}
