using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ITInventory.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSiteRoleAndCountryTopology : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SiteRole",
                schema: "dbo",
                table: "Servers",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Primary");

            migrationBuilder.AddColumn<string>(
                name: "SiteRole",
                schema: "dbo",
                table: "PhysicalDevices",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Primary");

            migrationBuilder.CreateTable(
                name: "CountryTopologyFiles",
                schema: "dbo",
                columns: table => new
                {
                    CountryId = table.Column<int>(type: "int", nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    FileData = table.Column<byte[]>(type: "varbinary(max)", nullable: false),
                    FileSize = table.Column<long>(type: "bigint", nullable: false),
                    UploadedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UploadedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CountryTopologyFiles", x => x.CountryId);
                    table.ForeignKey(
                        name: "FK_CountryTopologyFiles_Countries_CountryId",
                        column: x => x.CountryId,
                        principalSchema: "dbo",
                        principalTable: "Countries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CountryTopologyFiles",
                schema: "dbo");

            migrationBuilder.DropColumn(
                name: "SiteRole",
                schema: "dbo",
                table: "Servers");

            migrationBuilder.DropColumn(
                name: "SiteRole",
                schema: "dbo",
                table: "PhysicalDevices");
        }
    }
}
