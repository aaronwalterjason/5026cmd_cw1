using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace KOMA.Migrations
{
    /// <inheritdoc />
    public partial class StaffSchedule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ScheduleWeeks",
                columns: table => new
                {
                    WeekStart = table.Column<DateOnly>(type: "date", nullable: false),
                    ChangedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PublishedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PublishedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScheduleWeeks", x => x.WeekStart);
                });

            migrationBuilder.CreateTable(
                name: "Shifts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Starts = table.Column<TimeOnly>(type: "time", nullable: false),
                    Ends = table.Column<TimeOnly>(type: "time", nullable: false),
                    Needed = table.Column<int>(type: "int", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Shifts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ShiftAssignments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    ShiftId = table.Column<int>(type: "int", nullable: true),
                    IsLeave = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShiftAssignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ShiftAssignments_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ShiftAssignments_Shifts_ShiftId",
                        column: x => x.ShiftId,
                        principalTable: "Shifts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "Shifts",
                columns: new[] { "Id", "Ends", "IsActive", "Name", "Needed", "SortOrder", "Starts" },
                values: new object[,]
                {
                    { 1, new TimeOnly(16, 0, 0), true, "Morning", 2, 0, new TimeOnly(10, 0, 0) },
                    { 2, new TimeOnly(23, 0, 0), true, "Evening", 2, 1, new TimeOnly(16, 0, 0) }
                });

            migrationBuilder.CreateIndex(
                name: "IX_ShiftAssignments_Date",
                table: "ShiftAssignments",
                column: "Date");

            migrationBuilder.CreateIndex(
                name: "IX_ShiftAssignments_ShiftId",
                table: "ShiftAssignments",
                column: "ShiftId");

            migrationBuilder.CreateIndex(
                name: "IX_ShiftAssignments_UserId_Date",
                table: "ShiftAssignments",
                columns: new[] { "UserId", "Date" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ScheduleWeeks");

            migrationBuilder.DropTable(
                name: "ShiftAssignments");

            migrationBuilder.DropTable(
                name: "Shifts");
        }
    }
}
