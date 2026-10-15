using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KOMA.Migrations
{
    /// <inheritdoc />
    public partial class RepairTableShapeAndDiningAreaFloor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Add Shape only if this database does not already have it.
            migrationBuilder.Sql(@"
                IF COL_LENGTH('dbo.RestaurantTables', 'Shape') IS NULL
                BEGIN
                    ALTER TABLE [dbo].[RestaurantTables]
                    ADD [Shape] int NOT NULL
                        CONSTRAINT [DF_RestaurantTables_Shape] DEFAULT (0);
                END
            ");

                    // Add Floor only if this database does not already have it.
                    migrationBuilder.Sql(@"
                IF COL_LENGTH('dbo.DiningAreas', 'Floor') IS NULL
                BEGIN
                    ALTER TABLE [dbo].[DiningAreas]
                    ADD [Floor] nvarchar(30) NOT NULL
                        CONSTRAINT [DF_DiningAreas_Floor]
                        DEFAULT (N'Ground Floor');
                END
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Preserve these columns because they may have existed
            // before this repair on another team member's database.
        }
    }
}
