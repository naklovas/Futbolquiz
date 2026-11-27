using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ITInventory.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddLocationsTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Locations",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CountryId = table.Column<int>(type: "int", nullable: false),
                    Branch = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Class = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Locations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Locations_Countries_CountryId",
                        column: x => x.CountryId,
                        principalSchema: "dbo",
                        principalTable: "Countries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Locations_CountryId_Branch",
                schema: "dbo",
                table: "Locations",
                columns: new[] { "CountryId", "Branch" },
                unique: true);

            // Seed from the bank's real branch list, matched by exact Countries.Name.
            // Rows whose country isn't in Countries yet are silently skipped here --
            // add the country first, then use Admin > Locations > Import to load it.
            migrationBuilder.Sql(@"
                INSERT INTO dbo.Locations (CountryId, Branch, Class, IsActive, CreatedAt)
                SELECT c.Id, v.Branch, v.Class, 1, '2026-08-07T00:00:00'
                FROM (VALUES
    (N'İNGİLTERE', N'LONDRA', N'Yurtdışı Şube'),
    (N'KOSOVA', N'PRİŞTİNE', N'Yurtdışı Şube'),
    (N'KOSOVA', N'PRİZREN', N'Yurtdışı Şube'),
    (N'KOSOVA', N'PEJA', N'Yurtdışı Şube'),
    (N'KOSOVA', N'FERİZAJ', N'Yurtdışı Şube'),
    (N'BULGARİSTAN', N'YÖNETİCİLİK', N'Yurtdışı Şube'),
    (N'BULGARİSTAN', N'SOFYA', N'Yurtdışı Şube'),
    (N'BULGARİSTAN', N'FİLİBE', N'Yurtdışı Şube'),
    (N'BULGARİSTAN', N'VARNA', N'Yurtdışı Şube'),
    (N'BULGARİSTAN', N'KIRCAALİ', N'Yurtdışı Şube'),
    (N'IRAK', N'BAĞDAT', N'Yurtdışı Şube'),
    (N'IRAK', N'ERBİL', N'Yurtdışı Şube'),
    (N'YUNANİSTAN', N'ATİNA', N'Yurtdışı Şube'),
    (N'YUNANİSTAN', N'İSKEÇE', N'Yurtdışı Şube'),
    (N'YUNANİSTAN', N'GÜMÜLCİNE', N'Yurtdışı Şube'),
    (N'SUUDİ ARABİSTAN', N'CİDDE', N'Yurtdışı Şube'),
    (N'BAHREYN', N'MANAMA', N'Yurtdışı Şube'),
    (N'İRAN', N'TAHRAN', N'Yurtdışı Şube'),
    (N'KKTC', N'KKTC', N'Yurtdışı Şube'),
    (N'KKTC', N'Girne', N'Yurtdışı Şube'),
    (N'KKTC', N'Lefkoşa', N'Yurtdışı Şube'),
    (N'KKTC', N'Gazimağusa', N'Yurtdışı Şube'),
    (N'KKTC', N'Güzelyurt', N'Yurtdışı Şube'),
    (N'KKTC', N'Taşkınköy', N'Yurtdışı Şube'),
    (N'KKTC', N'Gönyeli', N'Yurtdışı Şube'),
    (N'KKTC', N'Karaoğlanoğlu', N'Yurtdışı Şube'),
    (N'KKTC', N'İskele', N'Yurtdışı Şube'),
    (N'ARNAVUTLUK', N'TİRAN', N'Yurtdışı Şube'),
    (N'ALMANYA', N'BERLIN', N'Yurtdışı İştirak'),
    (N'ALMANYA', N'DISBURG', N'Yurtdışı İştirak'),
    (N'ALMANYA', N'FRANKFURT', N'Yurtdışı İştirak'),
    (N'ALMANYA', N'Genel Müdürlük', N'Yurtdışı İştirak'),
    (N'ALMANYA', N'HAMBURG', N'Yurtdışı İştirak'),
    (N'ALMANYA', N'HANNOVER', N'Yurtdışı İştirak'),
    (N'ALMANYA', N'KÖLN', N'Yurtdışı İştirak'),
    (N'ALMANYA', N'MÜNİH', N'Yurtdışı İştirak'),
    (N'AZERBAYCAN', N'AHMET RECEPLİ', N'Yurtdışı İştirak'),
    (N'AZERBAYCAN', N'AZADLIG', N'Yurtdışı İştirak'),
    (N'AZERBAYCAN', N'BABEK', N'Yurtdışı İştirak'),
    (N'AZERBAYCAN', N'BAKÜ', N'Yurtdışı İştirak'),
    (N'AZERBAYCAN', N'GENCE', N'Yurtdışı İştirak'),
    (N'AZERBAYCAN', N'Genel Müdürlük', N'Yurtdışı İştirak'),
    (N'AZERBAYCAN', N'GUBA HİZMET NOKTASI', N'Yurtdışı İştirak'),
    (N'AZERBAYCAN', N'İÇERİŞEHİR', N'Yurtdışı İştirak'),
    (N'AZERBAYCAN', N'NAHÇIVAN', N'Yurtdışı İştirak'),
    (N'AZERBAYCAN', N'NEFCİLER ŞUBESİ', N'Yurtdışı İştirak'),
    (N'AZERBAYCAN', N'SAMED VURGUN', N'Yurtdışı İştirak'),
    (N'AZERBAYCAN', N'SEDEREK', N'Yurtdışı İştirak'),
    (N'AZERBAYCAN', N'SUMQAYT', N'Yurtdışı İştirak'),
    (N'BOSNA', N'BANJA LUKA ŞUBESİ', N'Yurtdışı İştirak'),
    (N'BOSNA', N'BIHAC ŞUBESİ', N'Yurtdışı İştirak'),
    (N'BOSNA', N'BIJELJINA ŞUBESİ', N'Yurtdışı İştirak'),
    (N'BOSNA', N'Bratunac', N'Yurtdışı İştirak'),
    (N'BOSNA', N'BRCKO ŞUBESİ', N'Yurtdışı İştirak'),
    (N'BOSNA', N'Cazin', N'Yurtdışı İştirak'),
    (N'BOSNA', N'Čelić', N'Yurtdışı İştirak'),
    (N'BOSNA', N'Derventa', N'Yurtdışı İştirak'),
    (N'BOSNA', N'DOBOJ OFİSİ', N'Yurtdışı İştirak'),
    (N'BOSNA', N'DOBRINJA ŞUBESİ', N'Yurtdışı İştirak'),
    (N'BOSNA', N'Donji Vakuf', N'Yurtdışı İştirak'),
    (N'BOSNA', N'FERHADIJA ŞUBESİ', N'Yurtdışı İştirak'),
    (N'BOSNA', N'Genel Müdürlük', N'Yurtdışı İştirak'),
    (N'BOSNA', N'GORADZE ŞUBESİ', N'Yurtdışı İştirak'),
    (N'BOSNA', N'Gračanica', N'Yurtdışı İştirak'),
    (N'BOSNA', N'Hadžići', N'Yurtdışı İştirak'),
    (N'BOSNA', N'ILIDZA ŞUBESİ', N'Yurtdışı İştirak'),
    (N'BOSNA', N'ILIJAŞ OFİSİ', N'Yurtdışı İştirak'),
    (N'BOSNA', N'JELAH ŞUBESİ', N'Yurtdışı İştirak'),
    (N'BOSNA', N'Kakanj', N'Yurtdışı İştirak'),
    (N'BOSNA', N'Konjic', N'Yurtdışı İştirak'),
    (N'BOSNA', N'Lukavac', N'Yurtdışı İştirak'),
    (N'BOSNA', N'Maglaj', N'Yurtdışı İştirak'),
    (N'BOSNA', N'MOSTAR ŞUBESİ', N'Yurtdışı İştirak'),
    (N'BOSNA', N'NOVİ GRAD ŞUBESİ', N'Yurtdışı İştirak'),
    (N'BOSNA', N'Novi Travnik', N'Yurtdışı İştirak'),
    (N'BOSNA', N'Sanski Most', N'Yurtdışı İştirak'),
    (N'BOSNA', N'SARAJEVO ŞUBESİ', N'Yurtdışı İştirak'),
    (N'BOSNA', N'Široki Brijeg', N'Yurtdışı İştirak'),
    (N'BOSNA', N'SREBRENİCA ŞUBESİ', N'Yurtdışı İştirak'),
    (N'BOSNA', N'Srebrenik', N'Yurtdışı İştirak'),
    (N'BOSNA', N'Teslić', N'Yurtdışı İştirak'),
    (N'BOSNA', N'TRAVNİK ŞUBESİ', N'Yurtdışı İştirak'),
    (N'BOSNA', N'TUZLA ŞUBESİ', N'Yurtdışı İştirak'),
    (N'BOSNA', N'Ustikolina', N'Yurtdışı İştirak'),
    (N'BOSNA', N'Visoko', N'Yurtdışı İştirak'),
    (N'BOSNA', N'VOGOSCA ŞUBESİ', N'Yurtdışı İştirak'),
    (N'BOSNA', N'Zenica', N'Yurtdışı İştirak'),
    (N'GÜRCİSTAN', N'Batum', N'Yurtdışı İştirak'),
    (N'GÜRCİSTAN', N'Genel Müdürlük', N'Yurtdışı İştirak'),
    (N'GÜRCİSTAN', N'Gldani', N'Yurtdışı İştirak'),
    (N'GÜRCİSTAN', N'Kutaisi', N'Yurtdışı İştirak'),
    (N'GÜRCİSTAN', N'Marneuli', N'Yurtdışı İştirak'),
    (N'GÜRCİSTAN', N'Tiflis', N'Yurtdışı İştirak'),
    (N'GÜRCİSTAN', N'Tsereteli', N'Yurtdışı İştirak'),
    (N'GÜRCİSTAN', N'Varketeli', N'Yurtdışı İştirak'),
    (N'KARADAĞ', N'BAR', N'Yurtdışı İştirak'),
    (N'KARADAĞ', N'BUDVA', N'Yurtdışı İştirak'),
    (N'KARADAĞ', N'Genel Müdürlük', N'Yurtdışı İştirak'),
    (N'KARADAĞ', N'PODGORITSA', N'Yurtdışı İştirak'),
    (N'KAZAKİSTAN', N'AKTAU', N'Yurtdışı İştirak'),
    (N'KAZAKİSTAN', N'ALMATY', N'Yurtdışı İştirak'),
    (N'KAZAKİSTAN', N'ALMATY-2', N'Yurtdışı İştirak'),
    (N'KAZAKİSTAN', N'ASTANA', N'Yurtdışı İştirak'),
    (N'KAZAKİSTAN', N'ATIRAU', N'Yurtdışı İştirak'),
    (N'KAZAKİSTAN', N'ÇİMENT', N'Yurtdışı İştirak'),
    (N'KAZAKİSTAN', N'Genel Müdürlük', N'Yurtdışı İştirak'),
    (N'KAZAKİSTAN', N'KARAGANDİ', N'Yurtdışı İştirak'),
    (N'KAZAKİSTAN', N'TÜRKİSTAN', N'Yurtdışı İştirak'),
    (N'ÖZBEKİSTAN', N'ANDİCAN', N'Yurtdışı İştirak'),
    (N'ÖZBEKİSTAN', N'BUHARA', N'Yurtdışı İştirak'),
    (N'ÖZBEKİSTAN', N'FERGANA', N'Yurtdışı İştirak'),
    (N'ÖZBEKİSTAN', N'Genel Müdürlük', N'Yurtdışı İştirak'),
    (N'ÖZBEKİSTAN', N'KURUMSAL ŞUBE', N'Yurtdışı İştirak'),
    (N'ÖZBEKİSTAN', N'OPERU', N'Yurtdışı İştirak'),
    (N'ÖZBEKİSTAN', N'SEMERKAND', N'Yurtdışı İştirak'),
    (N'ÖZBEKİSTAN', N'YUNUSABAD', N'Yurtdışı İştirak'),
    (N'RUSYA', N'Genel Müdürlük', N'Yurtdışı İştirak'),
    (N'TÜRKMENİSTAN', N'Genel Müdürlük', N'Yurtdışı İştirak')
                ) AS v(CountryName, Branch, Class)
                INNER JOIN dbo.Countries c ON c.Name = v.CountryName;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Locations",
                schema: "dbo");
        }
    }
}
