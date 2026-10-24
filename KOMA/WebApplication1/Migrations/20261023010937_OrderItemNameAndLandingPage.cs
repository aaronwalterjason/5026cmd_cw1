using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KOMA.Migrations
{
    /// <inheritdoc />
    public partial class OrderItemNameAndLandingPage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_OrderItems_MenuItems_MenuItemId",
                table: "OrderItems");

            migrationBuilder.AlterColumn<int>(
                name: "MenuItemId",
                table: "OrderItems",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AddColumn<string>(
                name: "ItemName",
                table: "OrderItems",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            // Give every existing order line the name of the dish it was ordered as, so past orders
            // keep showing it even after the dish is renamed or deleted from the menu.
            migrationBuilder.Sql(
                "UPDATE oi SET oi.ItemName = mi.Name FROM OrderItems oi JOIN MenuItems mi ON mi.Id = oi.MenuItemId;");

            migrationBuilder.CreateTable(
                name: "LandingPageContents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    HeroEyebrow = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    HeroTitle = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    HeroTitleAccent = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    HeroIntro = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    HeroButtonText = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    HeroImageUrl = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    FoodEyebrow = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    FoodTitle = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    FoodIntro = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    ShowRecommended = table.Column<bool>(type: "bit", nullable: false),
                    RecommendedTitle = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    ShowSeasonal = table.Column<bool>(type: "bit", nullable: false),
                    SeasonalTitle = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    ShowStory = table.Column<bool>(type: "bit", nullable: false),
                    StoryEyebrow = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    StoryTitle = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    StoryText = table.Column<string>(type: "nvarchar(600)", maxLength: 600, nullable: true),
                    ShowExperience = table.Column<bool>(type: "bit", nullable: false),
                    ExperienceEyebrow = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    ExperienceTitle = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Card1Eyebrow = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    Card1Title = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    Card1Text = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    Card2Eyebrow = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    Card2Title = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    Card2Text = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    Card3Eyebrow = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    Card3Title = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    Card3Text = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    InvitationHeading = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LandingPageContents", x => x.Id);
                });

            migrationBuilder.AddForeignKey(
                name: "FK_OrderItems_MenuItems_MenuItemId",
                table: "OrderItems",
                column: "MenuItemId",
                principalTable: "MenuItems",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_OrderItems_MenuItems_MenuItemId",
                table: "OrderItems");

            migrationBuilder.DropTable(
                name: "LandingPageContents");

            migrationBuilder.DropColumn(
                name: "ItemName",
                table: "OrderItems");

            migrationBuilder.AlterColumn<int>(
                name: "MenuItemId",
                table: "OrderItems",
                type: "int",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_OrderItems_MenuItems_MenuItemId",
                table: "OrderItems",
                column: "MenuItemId",
                principalTable: "MenuItems",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
