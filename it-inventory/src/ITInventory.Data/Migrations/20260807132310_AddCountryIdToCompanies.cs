using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ITInventory.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCountryIdToCompanies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Companies_Name",
                schema: "dbo",
                table: "Companies");

            migrationBuilder.AddColumn<int>(
                name: "CountryId",
                schema: "dbo",
                table: "Companies",
                type: "int",
                nullable: false,
                defaultValue: 0);

            // Any pre-existing Company rows (created before this migration) get CountryId = 0,
            // which would violate the FK below. Backfill them to the first available country.
            migrationBuilder.Sql(@"
                UPDATE dbo.Companies
                SET CountryId = (SELECT TOP 1 Id FROM dbo.Countries ORDER BY Id)
                WHERE CountryId = 0
                  AND EXISTS (SELECT 1 FROM dbo.Countries);
            ");

            migrationBuilder.CreateIndex(
                name: "IX_Companies_CountryId",
                schema: "dbo",
                table: "Companies",
                column: "CountryId");

            migrationBuilder.CreateIndex(
                name: "IX_Companies_Name_CountryId",
                schema: "dbo",
                table: "Companies",
                columns: new[] { "Name", "CountryId" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Companies_Countries_CountryId",
                schema: "dbo",
                table: "Companies",
                column: "CountryId",
                principalSchema: "dbo",
                principalTable: "Countries",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Companies_Countries_CountryId",
                schema: "dbo",
                table: "Companies");

            migrationBuilder.DropIndex(
                name: "IX_Companies_CountryId",
                schema: "dbo",
                table: "Companies");

            migrationBuilder.DropIndex(
                name: "IX_Companies_Name_CountryId",
                schema: "dbo",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "CountryId",
                schema: "dbo",
                table: "Companies");

            migrationBuilder.CreateIndex(
                name: "IX_Companies_Name",
                schema: "dbo",
                table: "Companies",
                column: "Name",
                unique: true);
        }
    }
}
