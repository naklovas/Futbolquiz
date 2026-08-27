using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ITInventory.Data.Migrations
{
    /// <inheritdoc />
    public partial class RenameServerCategory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                schema: "dbo",
                table: "DeviceCategories",
                keyColumn: "Id",
                keyValue: 1,
                column: "Name",
                value: "ESXi / Physical Server");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                schema: "dbo",
                table: "DeviceCategories",
                keyColumn: "Id",
                keyValue: 1,
                column: "Name",
                value: "Server");
        }
    }
}
