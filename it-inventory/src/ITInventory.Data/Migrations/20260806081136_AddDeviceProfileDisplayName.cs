using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ITInventory.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDeviceProfileDisplayName : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DisplayName",
                schema: "dbo",
                table: "DeviceProfileCatalog",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.UpdateData(
                schema: "dbo",
                table: "DeviceCategories",
                keyColumn: "Id",
                keyValue: 1,
                column: "Name",
                value: "Server");

            migrationBuilder.UpdateData(
                schema: "dbo",
                table: "DeviceCategories",
                keyColumn: "Id",
                keyValue: 2,
                column: "Name",
                value: "Network Device");

            migrationBuilder.UpdateData(
                schema: "dbo",
                table: "DeviceCategories",
                keyColumn: "Id",
                keyValue: 3,
                column: "Name",
                value: "Security");

            migrationBuilder.UpdateData(
                schema: "dbo",
                table: "DeviceCategories",
                keyColumn: "Id",
                keyValue: 4,
                column: "Name",
                value: "Audio/Video");

            migrationBuilder.UpdateData(
                schema: "dbo",
                table: "DeviceCategories",
                keyColumn: "Id",
                keyValue: 5,
                column: "Name",
                value: "Storage");

            migrationBuilder.UpdateData(
                schema: "dbo",
                table: "DeviceCategories",
                keyColumn: "Id",
                keyValue: 6,
                column: "Name",
                value: "Printer");

            migrationBuilder.UpdateData(
                schema: "dbo",
                table: "DeviceCategories",
                keyColumn: "Id",
                keyValue: 7,
                column: "Name",
                value: "Client");

            migrationBuilder.UpdateData(
                schema: "dbo",
                table: "DeviceCategories",
                keyColumn: "Id",
                keyValue: 8,
                column: "Name",
                value: "Virtualization");

            migrationBuilder.UpdateData(
                schema: "dbo",
                table: "DeviceCategories",
                keyColumn: "Id",
                keyValue: 9,
                column: "Name",
                value: "Power/Infrastructure");

            migrationBuilder.UpdateData(
                schema: "dbo",
                table: "DeviceCategories",
                keyColumn: "Id",
                keyValue: 10,
                column: "Name",
                value: "Other");

            migrationBuilder.UpdateData(
                schema: "dbo",
                table: "DeviceProfileCatalog",
                keyColumn: "Id",
                keyValue: 1,
                column: "DisplayName",
                value: "Data Storage (NAS)");

            migrationBuilder.UpdateData(
                schema: "dbo",
                table: "DeviceProfileCatalog",
                keyColumn: "Id",
                keyValue: 2,
                column: "DisplayName",
                value: "Security Cameras (CCTV / NVR)");

            migrationBuilder.UpdateData(
                schema: "dbo",
                table: "DeviceProfileCatalog",
                keyColumn: "Id",
                keyValue: 3,
                column: "DisplayName",
                value: "Server / Appliance (Linux)");

            migrationBuilder.UpdateData(
                schema: "dbo",
                table: "DeviceProfileCatalog",
                keyColumn: "Id",
                keyValue: 4,
                column: "DisplayName",
                value: "IP Phone (VoIP)");

            migrationBuilder.UpdateData(
                schema: "dbo",
                table: "DeviceProfileCatalog",
                keyColumn: "Id",
                keyValue: 5,
                column: "DisplayName",
                value: "Wireless Network (Access Point)");

            migrationBuilder.UpdateData(
                schema: "dbo",
                table: "DeviceProfileCatalog",
                keyColumn: "Id",
                keyValue: 6,
                column: "DisplayName",
                value: "Network Device (SAN Switch)");

            migrationBuilder.UpdateData(
                schema: "dbo",
                table: "DeviceProfileCatalog",
                keyColumn: "Id",
                keyValue: 7,
                column: "DisplayName",
                value: "Data Storage (Storage)");

            migrationBuilder.UpdateData(
                schema: "dbo",
                table: "DeviceProfileCatalog",
                keyColumn: "Id",
                keyValue: 8,
                column: "DisplayName",
                value: "Data Storage (Storage Server)");

            migrationBuilder.UpdateData(
                schema: "dbo",
                table: "DeviceProfileCatalog",
                keyColumn: "Id",
                keyValue: 9,
                column: "DisplayName",
                value: "Data Storage (Storage / NAS)");

            migrationBuilder.UpdateData(
                schema: "dbo",
                table: "DeviceProfileCatalog",
                keyColumn: "Id",
                keyValue: 10,
                column: "DisplayName",
                value: "Industrial Gateway (IoT)");

            migrationBuilder.UpdateData(
                schema: "dbo",
                table: "DeviceProfileCatalog",
                keyColumn: "Id",
                keyValue: 11,
                column: "DisplayName",
                value: "Server Management Card (OOB / Console)");

            migrationBuilder.UpdateData(
                schema: "dbo",
                table: "DeviceProfileCatalog",
                keyColumn: "Id",
                keyValue: 12,
                column: "DisplayName",
                value: "Virtualization (Container Host)");

            migrationBuilder.UpdateData(
                schema: "dbo",
                table: "DeviceProfileCatalog",
                keyColumn: "Id",
                keyValue: 13,
                column: "DisplayName",
                value: "Load Balancer");

            migrationBuilder.UpdateData(
                schema: "dbo",
                table: "DeviceProfileCatalog",
                keyColumn: "Id",
                keyValue: 14,
                column: "DisplayName",
                value: "Unmapped");

            migrationBuilder.UpdateData(
                schema: "dbo",
                table: "DeviceProfileCatalog",
                keyColumn: "Id",
                keyValue: 15,
                column: "DisplayName",
                value: "Network Device (Router)");

            migrationBuilder.UpdateData(
                schema: "dbo",
                table: "DeviceProfileCatalog",
                keyColumn: "Id",
                keyValue: 16,
                column: "DisplayName",
                value: "Printer (Printer / Print Server)");

            migrationBuilder.UpdateData(
                schema: "dbo",
                table: "DeviceProfileCatalog",
                keyColumn: "Id",
                keyValue: 17,
                column: "DisplayName",
                value: "Hyperconverged Infrastructure (HCI)");

            migrationBuilder.UpdateData(
                schema: "dbo",
                table: "DeviceProfileCatalog",
                keyColumn: "Id",
                keyValue: 18,
                column: "DisplayName",
                value: "Server Management (Console Server / OOB)");

            migrationBuilder.UpdateData(
                schema: "dbo",
                table: "DeviceProfileCatalog",
                keyColumn: "Id",
                keyValue: 19,
                column: "DisplayName",
                value: "Network Services (SD-WAN)");

            migrationBuilder.UpdateData(
                schema: "dbo",
                table: "DeviceProfileCatalog",
                keyColumn: "Id",
                keyValue: 20,
                column: "DisplayName",
                value: "Virtualization (Management Server)");

            migrationBuilder.UpdateData(
                schema: "dbo",
                table: "DeviceProfileCatalog",
                keyColumn: "Id",
                keyValue: 21,
                column: "DisplayName",
                value: "UPS / Power Management");

            migrationBuilder.UpdateData(
                schema: "dbo",
                table: "DeviceProfileCatalog",
                keyColumn: "Id",
                keyValue: 22,
                column: "DisplayName",
                value: "Server (Linux/SAP)");

            migrationBuilder.UpdateData(
                schema: "dbo",
                table: "DeviceProfileCatalog",
                keyColumn: "Id",
                keyValue: 23,
                column: "DisplayName",
                value: "Server (Linux)");

            migrationBuilder.UpdateData(
                schema: "dbo",
                table: "DeviceProfileCatalog",
                keyColumn: "Id",
                keyValue: 24,
                column: "DisplayName",
                value: "Server Management (Console Server)");

            migrationBuilder.UpdateData(
                schema: "dbo",
                table: "DeviceProfileCatalog",
                keyColumn: "Id",
                keyValue: 25,
                column: "DisplayName",
                value: "Server (Unix)");

            migrationBuilder.UpdateData(
                schema: "dbo",
                table: "DeviceProfileCatalog",
                keyColumn: "Id",
                keyValue: 26,
                column: "DisplayName",
                value: "Security / Network Device");

            migrationBuilder.UpdateData(
                schema: "dbo",
                table: "DeviceProfileCatalog",
                keyColumn: "Id",
                keyValue: 27,
                column: "DisplayName",
                value: "Network Device (Switch / Router)");

            migrationBuilder.UpdateData(
                schema: "dbo",
                table: "DeviceProfileCatalog",
                keyColumn: "Id",
                keyValue: 28,
                column: "DisplayName",
                value: "Security (Firewall)");

            migrationBuilder.UpdateData(
                schema: "dbo",
                table: "DeviceProfileCatalog",
                keyColumn: "Id",
                keyValue: 29,
                column: "DisplayName",
                value: "IP Phone / PBX");

            migrationBuilder.UpdateData(
                schema: "dbo",
                table: "DeviceProfileCatalog",
                keyColumn: "Id",
                keyValue: 30,
                column: "DisplayName",
                value: "Network Device (General)");

            migrationBuilder.UpdateData(
                schema: "dbo",
                table: "DeviceProfileCatalog",
                keyColumn: "Id",
                keyValue: 31,
                column: "DisplayName",
                value: "Network Device (Switch)");

            migrationBuilder.UpdateData(
                schema: "dbo",
                table: "DeviceProfileCatalog",
                keyColumn: "Id",
                keyValue: 32,
                column: "DisplayName",
                value: "Printer");

            migrationBuilder.UpdateData(
                schema: "dbo",
                table: "DeviceProfileCatalog",
                keyColumn: "Id",
                keyValue: 33,
                column: "DisplayName",
                value: "Server Management Card (OOB)");

            migrationBuilder.UpdateData(
                schema: "dbo",
                table: "DeviceProfileCatalog",
                keyColumn: "Id",
                keyValue: 34,
                column: "DisplayName",
                value: "Printer (Print Server)");

            migrationBuilder.UpdateData(
                schema: "dbo",
                table: "DeviceProfileCatalog",
                keyColumn: "Id",
                keyValue: 35,
                column: "DisplayName",
                value: "Server / Appliance (Linux/IoT)");

            migrationBuilder.UpdateData(
                schema: "dbo",
                table: "DeviceProfileCatalog",
                keyColumn: "Id",
                keyValue: 36,
                column: "DisplayName",
                value: "Virtualization (Hypervisor)");

            migrationBuilder.UpdateData(
                schema: "dbo",
                table: "DeviceProfileCatalog",
                keyColumn: "Id",
                keyValue: 37,
                column: "DisplayName",
                value: "Security Cameras (CCTV)");

            migrationBuilder.UpdateData(
                schema: "dbo",
                table: "DeviceProfileCatalog",
                keyColumn: "Id",
                keyValue: 38,
                column: "DisplayName",
                value: "Server (Windows)");

            migrationBuilder.UpdateData(
                schema: "dbo",
                table: "DeviceProfileCatalog",
                keyColumn: "Id",
                keyValue: 39,
                column: "DisplayName",
                value: "Media Player / Smart Display");

            migrationBuilder.UpdateData(
                schema: "dbo",
                table: "DeviceProfileCatalog",
                keyColumn: "Id",
                keyValue: 40,
                column: "DisplayName",
                value: "Network Services (DDI)");

            migrationBuilder.UpdateData(
                schema: "dbo",
                table: "DeviceProfileCatalog",
                keyColumn: "Id",
                keyValue: 41,
                column: "DisplayName",
                value: "Client Computer (Workstation)");

            migrationBuilder.UpdateData(
                schema: "dbo",
                table: "DeviceProfileCatalog",
                keyColumn: "Id",
                keyValue: 42,
                column: "DisplayName",
                value: "IP Phone (Analog Gateway)");

            migrationBuilder.UpdateData(
                schema: "dbo",
                table: "DeviceProfileCatalog",
                keyColumn: "Id",
                keyValue: 43,
                column: "DisplayName",
                value: "Security (Appliance)");

            migrationBuilder.UpdateData(
                schema: "dbo",
                table: "DeviceProfileCatalog",
                keyColumn: "Id",
                keyValue: 44,
                column: "DisplayName",
                value: "Client Computer (Workstation / Media)");

            migrationBuilder.UpdateData(
                schema: "dbo",
                table: "DeviceProfileCatalog",
                keyColumn: "Id",
                keyValue: 45,
                column: "DisplayName",
                value: "Server (Unix/Mainframe)");

            migrationBuilder.UpdateData(
                schema: "dbo",
                table: "DeviceProfileCatalog",
                keyColumn: "Id",
                keyValue: 46,
                column: "DisplayName",
                value: "Network Device (Switch / HCI)");

            migrationBuilder.UpdateData(
                schema: "dbo",
                table: "DeviceProfileCatalog",
                keyColumn: "Id",
                keyValue: 47,
                column: "DisplayName",
                value: "Network Device (Router / Firewall)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DisplayName",
                schema: "dbo",
                table: "DeviceProfileCatalog");

            migrationBuilder.UpdateData(
                schema: "dbo",
                table: "DeviceCategories",
                keyColumn: "Id",
                keyValue: 1,
                column: "Name",
                value: "Sunucu");

            migrationBuilder.UpdateData(
                schema: "dbo",
                table: "DeviceCategories",
                keyColumn: "Id",
                keyValue: 2,
                column: "Name",
                value: "Ağ Cihazı");

            migrationBuilder.UpdateData(
                schema: "dbo",
                table: "DeviceCategories",
                keyColumn: "Id",
                keyValue: 3,
                column: "Name",
                value: "Güvenlik");

            migrationBuilder.UpdateData(
                schema: "dbo",
                table: "DeviceCategories",
                keyColumn: "Id",
                keyValue: 4,
                column: "Name",
                value: "Ses/Görüntü");

            migrationBuilder.UpdateData(
                schema: "dbo",
                table: "DeviceCategories",
                keyColumn: "Id",
                keyValue: 5,
                column: "Name",
                value: "Depolama");

            migrationBuilder.UpdateData(
                schema: "dbo",
                table: "DeviceCategories",
                keyColumn: "Id",
                keyValue: 6,
                column: "Name",
                value: "Yazıcı");

            migrationBuilder.UpdateData(
                schema: "dbo",
                table: "DeviceCategories",
                keyColumn: "Id",
                keyValue: 7,
                column: "Name",
                value: "İstemci");

            migrationBuilder.UpdateData(
                schema: "dbo",
                table: "DeviceCategories",
                keyColumn: "Id",
                keyValue: 8,
                column: "Name",
                value: "Sanallaştırma");

            migrationBuilder.UpdateData(
                schema: "dbo",
                table: "DeviceCategories",
                keyColumn: "Id",
                keyValue: 9,
                column: "Name",
                value: "Güç/Altyapı");

            migrationBuilder.UpdateData(
                schema: "dbo",
                table: "DeviceCategories",
                keyColumn: "Id",
                keyValue: 10,
                column: "Name",
                value: "Diğer");
        }
    }
}
