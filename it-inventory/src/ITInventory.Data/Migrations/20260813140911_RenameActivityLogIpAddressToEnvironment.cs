using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ITInventory.Data.Migrations
{
    /// <inheritdoc />
    public partial class RenameActivityLogIpAddressToEnvironment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "IpAddress",
                schema: "dbo",
                table: "ActivityLogs",
                newName: "EnvironmentName");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "EnvironmentName",
                schema: "dbo",
                table: "ActivityLogs",
                newName: "IpAddress");
        }
    }
}
