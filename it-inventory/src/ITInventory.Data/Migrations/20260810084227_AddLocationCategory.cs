using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ITInventory.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddLocationCategory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "LocationCategory",
                schema: "dbo",
                table: "Servers",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Local");

            migrationBuilder.AddColumn<string>(
                name: "LocationCategory",
                schema: "dbo",
                table: "PhysicalDevices",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Local");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LocationCategory",
                schema: "dbo",
                table: "Servers");

            migrationBuilder.DropColumn(
                name: "LocationCategory",
                schema: "dbo",
                table: "PhysicalDevices");
        }
    }
}
