using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ITInventory.Data.Migrations
{
    /// <inheritdoc />
    public partial class SplitServerIntoHostAndEndpoints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Servers_Applications_ApplicationId",
                schema: "dbo",
                table: "Servers");

            migrationBuilder.DropIndex(
                name: "IX_Servers_ApplicationId",
                schema: "dbo",
                table: "Servers");

            migrationBuilder.CreateTable(
                name: "ServerEndpoints",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ServerId = table.Column<int>(type: "int", nullable: false),
                    IpAddress = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Port = table.Column<int>(type: "int", nullable: true),
                    ApplicationId = table.Column<int>(type: "int", nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServerEndpoints", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ServerEndpoints_Applications_ApplicationId",
                        column: x => x.ApplicationId,
                        principalSchema: "dbo",
                        principalTable: "Applications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_ServerEndpoints_Servers_ServerId",
                        column: x => x.ServerId,
                        principalSchema: "dbo",
                        principalTable: "Servers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ServerEndpoints_ApplicationId",
                schema: "dbo",
                table: "ServerEndpoints",
                column: "ApplicationId");

            migrationBuilder.CreateIndex(
                name: "IX_ServerEndpoints_ServerId",
                schema: "dbo",
                table: "ServerEndpoints",
                column: "ServerId");

            // Carry over each existing Server's IP/Port/Application into its own ServerEndpoint row
            // BEFORE the source columns are dropped below, so no data is lost.
            migrationBuilder.Sql(@"
                INSERT INTO dbo.ServerEndpoints (ServerId, IpAddress, Port, ApplicationId, CreatedAt)
                SELECT Id, IpAddress, Port, ApplicationId, SYSUTCDATETIME()
                FROM dbo.Servers
                WHERE IpAddress IS NOT NULL OR Port IS NOT NULL OR ApplicationId IS NOT NULL;
            ");

            migrationBuilder.DropColumn(
                name: "ApplicationId",
                schema: "dbo",
                table: "Servers");

            migrationBuilder.DropColumn(
                name: "IpAddress",
                schema: "dbo",
                table: "Servers");

            migrationBuilder.DropColumn(
                name: "Port",
                schema: "dbo",
                table: "Servers");

            migrationBuilder.AddColumn<int>(
                name: "HostPhysicalDeviceId",
                schema: "dbo",
                table: "Servers",
                type: "int",
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Location",
                schema: "dbo",
                table: "Servers",
                type: "nvarchar(255)",
                maxLength: 255,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(255)",
                oldMaxLength: 255);

            migrationBuilder.CreateIndex(
                name: "IX_Servers_HostPhysicalDeviceId",
                schema: "dbo",
                table: "Servers",
                column: "HostPhysicalDeviceId");

            migrationBuilder.AddForeignKey(
                name: "FK_Servers_PhysicalDevices_HostPhysicalDeviceId",
                schema: "dbo",
                table: "Servers",
                column: "HostPhysicalDeviceId",
                principalSchema: "dbo",
                principalTable: "PhysicalDevices",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Servers_PhysicalDevices_HostPhysicalDeviceId",
                schema: "dbo",
                table: "Servers");

            migrationBuilder.DropIndex(
                name: "IX_Servers_HostPhysicalDeviceId",
                schema: "dbo",
                table: "Servers");

            migrationBuilder.DropColumn(
                name: "HostPhysicalDeviceId",
                schema: "dbo",
                table: "Servers");

            migrationBuilder.AddColumn<int>(
                name: "ApplicationId",
                schema: "dbo",
                table: "Servers",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IpAddress",
                schema: "dbo",
                table: "Servers",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Port",
                schema: "dbo",
                table: "Servers",
                type: "int",
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Location",
                schema: "dbo",
                table: "Servers",
                type: "nvarchar(255)",
                maxLength: 255,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "nvarchar(255)",
                oldMaxLength: 255,
                oldNullable: true);

            migrationBuilder.DropTable(
                name: "ServerEndpoints",
                schema: "dbo");

            migrationBuilder.CreateIndex(
                name: "IX_Servers_ApplicationId",
                schema: "dbo",
                table: "Servers",
                column: "ApplicationId");

            migrationBuilder.AddForeignKey(
                name: "FK_Servers_Applications_ApplicationId",
                schema: "dbo",
                table: "Servers",
                column: "ApplicationId",
                principalSchema: "dbo",
                principalTable: "Applications",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }
    }
}
