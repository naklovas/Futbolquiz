using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ITInventory.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCountryDisplayName : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DisplayName",
                schema: "dbo",
                table: "Countries",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DisplayName",
                schema: "dbo",
                table: "Countries");
        }
    }
}
