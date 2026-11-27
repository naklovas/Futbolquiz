using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace ITInventory.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "dbo");

            migrationBuilder.CreateTable(
                name: "Countries",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Code = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Countries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DeviceCategories",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeviceCategories", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Circuits",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CountryId = table.Column<int>(type: "int", nullable: false),
                    CircuitType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    CircuitCapacity = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Provider = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    Branch = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    Location = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    StartDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    EndDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Circuits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Circuits_Countries_CountryId",
                        column: x => x.CountryId,
                        principalSchema: "dbo",
                        principalTable: "Countries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Licenses",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CountryId = table.Column<int>(type: "int", nullable: false),
                    LicenseName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    VendorSupplier = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    Branch = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    Location = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    SupportStartDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    SupportEndDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Licenses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Licenses_Countries_CountryId",
                        column: x => x.CountryId,
                        principalSchema: "dbo",
                        principalTable: "Countries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DeviceProfileCatalog",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProfileName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    CategoryId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeviceProfileCatalog", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DeviceProfileCatalog_DeviceCategories_CategoryId",
                        column: x => x.CategoryId,
                        principalSchema: "dbo",
                        principalTable: "DeviceCategories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "PhysicalDevices",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CountryId = table.Column<int>(type: "int", nullable: false),
                    CategoryId = table.Column<int>(type: "int", nullable: false),
                    DeviceProfileId = table.Column<int>(type: "int", nullable: true),
                    SourceZiraatYdId = table.Column<int>(type: "int", nullable: true),
                    DeviceName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    Brand = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Model = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    ApplianceType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    SoftwareVersion = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    SerialNo = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    IpAddress = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    MgmtIp = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Branch = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    Location = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    VendorSupplier = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    LicenceInfo = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    StartOfSupportDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    EndOfSupportDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    EndOfLifeDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PhysicalDevices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PhysicalDevices_Countries_CountryId",
                        column: x => x.CountryId,
                        principalSchema: "dbo",
                        principalTable: "Countries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PhysicalDevices_DeviceCategories_CategoryId",
                        column: x => x.CategoryId,
                        principalSchema: "dbo",
                        principalTable: "DeviceCategories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PhysicalDevices_DeviceProfileCatalog_DeviceProfileId",
                        column: x => x.DeviceProfileId,
                        principalSchema: "dbo",
                        principalTable: "DeviceProfileCatalog",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "Servers",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CountryId = table.Column<int>(type: "int", nullable: false),
                    DeviceProfileId = table.Column<int>(type: "int", nullable: true),
                    SourceZiraatYdId = table.Column<int>(type: "int", nullable: true),
                    HostName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    ApplianceType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    IpAddress = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    OperatingSystem = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    Brand = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Model = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    SerialNo = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    VendorSupplier = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    Port = table.Column<int>(type: "int", nullable: true),
                    Branch = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    Location = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    StartOfSupportDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    EndOfSupportDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    EndOfLifeDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Servers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Servers_Countries_CountryId",
                        column: x => x.CountryId,
                        principalSchema: "dbo",
                        principalTable: "Countries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Servers_DeviceProfileCatalog_DeviceProfileId",
                        column: x => x.DeviceProfileId,
                        principalSchema: "dbo",
                        principalTable: "DeviceProfileCatalog",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.InsertData(
                schema: "dbo",
                table: "DeviceCategories",
                columns: new[] { "Id", "Name" },
                values: new object[,]
                {
                    { 1, "Sunucu" },
                    { 2, "Ağ Cihazı" },
                    { 3, "Güvenlik" },
                    { 4, "Ses/Görüntü" },
                    { 5, "Depolama" },
                    { 6, "Yazıcı" },
                    { 7, "İstemci" },
                    { 8, "Sanallaştırma" },
                    { 9, "Güç/Altyapı" },
                    { 10, "Diğer" }
                });

            migrationBuilder.InsertData(
                schema: "dbo",
                table: "DeviceProfileCatalog",
                columns: new[] { "Id", "CategoryId", "ProfileName" },
                values: new object[,]
                {
                    { 14, null, "NULL" },
                    { 1, 5, "Veri Depolama (NAS)" },
                    { 2, 3, "Güvenlik Kameraları (CCTV / NVR)" },
                    { 3, 1, "Sunucu / Appliance (Linux)" },
                    { 4, 4, "IP Telefon (VoIP)" },
                    { 5, 2, "Kablosuz Ağ (Access Point)" },
                    { 6, 2, "Ağ Cihazı (SAN Switch)" },
                    { 7, 5, "Veri Depolama (Storage)" },
                    { 8, 5, "Veri Depolama (Storage Server)" },
                    { 9, 5, "Veri Depolama (Storage / NAS)" },
                    { 10, 2, "Endüstriyel Ağ Geçidi (IoT)" },
                    { 11, 1, "Sunucu Yönetim Kartı (OOB / Console)" },
                    { 12, 8, "Sanallaştırma (Container Host)" },
                    { 13, 2, "Yük Dengeleyici (Load Balancer)" },
                    { 15, 2, "Ağ Cihazı (Router)" },
                    { 16, 6, "Yazıcı (Printer / Print Server)" },
                    { 17, 1, "Bütünleşik Sistem (HCI)" },
                    { 18, 1, "Sunucu Yönetim (Console Server / OOB)" },
                    { 19, 2, "Ağ Servisleri (SD-WAN)" },
                    { 20, 8, "Sanallaştırma (Yönetim Sunucusu)" },
                    { 21, 9, "UPS / Güç Yönetimi" },
                    { 22, 1, "Sunucu (Linux/SAP)" },
                    { 23, 1, "Sunucu (Linux)" },
                    { 24, 1, "Sunucu Yönetim (Console Server)" },
                    { 25, 1, "Sunucu (Unix)" },
                    { 26, 3, "Güvenlik / Ağ Cihazı" },
                    { 27, 2, "Ağ Cihazı (Switch / Router)" },
                    { 28, 3, "Güvenlik (Firewall)" },
                    { 29, 4, "IP Telefon / Santral" },
                    { 30, 2, "Ağ Cihazı (Genel)" },
                    { 31, 2, "Ağ Cihazı (Switch)" },
                    { 32, 6, "Yazıcı (Printer)" },
                    { 33, 1, "Sunucu Yönetim Kartı (OOB)" },
                    { 34, 6, "Yazıcı (Print Server)" },
                    { 35, 1, "Sunucu / Appliance (Linux/IoT)" },
                    { 36, 8, "Sanallaştırma (Hypervisor)" },
                    { 37, 3, "Güvenlik Kameraları (CCTV)" },
                    { 38, 1, "Sunucu (Windows)" },
                    { 39, 4, "Medya Oynatıcı / Akıllı Ekran" },
                    { 40, 2, "Ağ Servisleri (DDI)" },
                    { 41, 7, "İstemci Bilgisayar (Workstation)" },
                    { 42, 4, "IP Telefon (Analog Gateway)" },
                    { 43, 3, "Güvenlik (Appliance)" },
                    { 44, 7, "İstemci Bilgisayar (Workstation / Medya)" },
                    { 45, 1, "Sunucu (Unix/Mainframe)" },
                    { 46, 2, "Ağ Cihazı (Switch / HCI)" },
                    { 47, 3, "Ağ Cihazı (Router / Firewall)" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_Circuits_CountryId",
                schema: "dbo",
                table: "Circuits",
                column: "CountryId");

            migrationBuilder.CreateIndex(
                name: "IX_Countries_Name",
                schema: "dbo",
                table: "Countries",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DeviceCategories_Name",
                schema: "dbo",
                table: "DeviceCategories",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DeviceProfileCatalog_CategoryId",
                schema: "dbo",
                table: "DeviceProfileCatalog",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_DeviceProfileCatalog_ProfileName",
                schema: "dbo",
                table: "DeviceProfileCatalog",
                column: "ProfileName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Licenses_CountryId",
                schema: "dbo",
                table: "Licenses",
                column: "CountryId");

            migrationBuilder.CreateIndex(
                name: "IX_PhysicalDevices_CategoryId",
                schema: "dbo",
                table: "PhysicalDevices",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_PhysicalDevices_CountryId",
                schema: "dbo",
                table: "PhysicalDevices",
                column: "CountryId");

            migrationBuilder.CreateIndex(
                name: "IX_PhysicalDevices_DeviceProfileId",
                schema: "dbo",
                table: "PhysicalDevices",
                column: "DeviceProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_Servers_CountryId",
                schema: "dbo",
                table: "Servers",
                column: "CountryId");

            migrationBuilder.CreateIndex(
                name: "IX_Servers_DeviceProfileId",
                schema: "dbo",
                table: "Servers",
                column: "DeviceProfileId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Circuits",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "Licenses",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "PhysicalDevices",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "Servers",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "Countries",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "DeviceProfileCatalog",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "DeviceCategories",
                schema: "dbo");
        }
    }
}
