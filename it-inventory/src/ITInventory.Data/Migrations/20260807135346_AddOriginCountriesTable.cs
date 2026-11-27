using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ITInventory.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddOriginCountriesTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OriginCountries",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OriginCountries", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OriginCountries_Name",
                schema: "dbo",
                table: "OriginCountries",
                column: "Name",
                unique: true);

            var seedDate = new DateTime(2026, 8, 7, 0, 0, 0, DateTimeKind.Utc);
            migrationBuilder.InsertData(
                schema: "dbo",
                table: "OriginCountries",
                columns: new[] { "Name", "IsActive", "CreatedAt" },
                values: new object[,]
                {
                    { "Afghanistan", true, seedDate },
                    { "Albania", true, seedDate },
                    { "Algeria", true, seedDate },
                    { "Andorra", true, seedDate },
                    { "Angola", true, seedDate },
                    { "Argentina", true, seedDate },
                    { "Armenia", true, seedDate },
                    { "Australia", true, seedDate },
                    { "Austria", true, seedDate },
                    { "Azerbaijan", true, seedDate },
                    { "Bahamas", true, seedDate },
                    { "Bahrain", true, seedDate },
                    { "Bangladesh", true, seedDate },
                    { "Barbados", true, seedDate },
                    { "Belarus", true, seedDate },
                    { "Belgium", true, seedDate },
                    { "Belize", true, seedDate },
                    { "Benin", true, seedDate },
                    { "Bhutan", true, seedDate },
                    { "Bolivia", true, seedDate },
                    { "Bosnia and Herzegovina", true, seedDate },
                    { "Botswana", true, seedDate },
                    { "Brazil", true, seedDate },
                    { "Brunei", true, seedDate },
                    { "Bulgaria", true, seedDate },
                    { "Burkina Faso", true, seedDate },
                    { "Burundi", true, seedDate },
                    { "Cabo Verde", true, seedDate },
                    { "Cambodia", true, seedDate },
                    { "Cameroon", true, seedDate },
                    { "Canada", true, seedDate },
                    { "Central African Republic", true, seedDate },
                    { "Chad", true, seedDate },
                    { "Chile", true, seedDate },
                    { "China", true, seedDate },
                    { "Colombia", true, seedDate },
                    { "Comoros", true, seedDate },
                    { "Congo", true, seedDate },
                    { "Costa Rica", true, seedDate },
                    { "Croatia", true, seedDate },
                    { "Cuba", true, seedDate },
                    { "Cyprus", true, seedDate },
                    { "Czechia", true, seedDate },
                    { "Democratic Republic of the Congo", true, seedDate },
                    { "Denmark", true, seedDate },
                    { "Djibouti", true, seedDate },
                    { "Dominica", true, seedDate },
                    { "Dominican Republic", true, seedDate },
                    { "Ecuador", true, seedDate },
                    { "Egypt", true, seedDate },
                    { "El Salvador", true, seedDate },
                    { "Equatorial Guinea", true, seedDate },
                    { "Eritrea", true, seedDate },
                    { "Estonia", true, seedDate },
                    { "Eswatini", true, seedDate },
                    { "Ethiopia", true, seedDate },
                    { "Fiji", true, seedDate },
                    { "Finland", true, seedDate },
                    { "France", true, seedDate },
                    { "Gabon", true, seedDate },
                    { "Gambia", true, seedDate },
                    { "Georgia", true, seedDate },
                    { "Germany", true, seedDate },
                    { "Ghana", true, seedDate },
                    { "Greece", true, seedDate },
                    { "Grenada", true, seedDate },
                    { "Guatemala", true, seedDate },
                    { "Guinea", true, seedDate },
                    { "Guinea-Bissau", true, seedDate },
                    { "Guyana", true, seedDate },
                    { "Haiti", true, seedDate },
                    { "Honduras", true, seedDate },
                    { "Hungary", true, seedDate },
                    { "Iceland", true, seedDate },
                    { "India", true, seedDate },
                    { "Indonesia", true, seedDate },
                    { "Iran", true, seedDate },
                    { "Iraq", true, seedDate },
                    { "Ireland", true, seedDate },
                    { "Israel", true, seedDate },
                    { "Italy", true, seedDate },
                    { "Ivory Coast", true, seedDate },
                    { "Jamaica", true, seedDate },
                    { "Japan", true, seedDate },
                    { "Jordan", true, seedDate },
                    { "Kazakhstan", true, seedDate },
                    { "Kenya", true, seedDate },
                    { "Kiribati", true, seedDate },
                    { "Kosovo", true, seedDate },
                    { "Kuwait", true, seedDate },
                    { "Kyrgyzstan", true, seedDate },
                    { "Laos", true, seedDate },
                    { "Latvia", true, seedDate },
                    { "Lebanon", true, seedDate },
                    { "Lesotho", true, seedDate },
                    { "Liberia", true, seedDate },
                    { "Libya", true, seedDate },
                    { "Liechtenstein", true, seedDate },
                    { "Lithuania", true, seedDate },
                    { "Luxembourg", true, seedDate },
                    { "Madagascar", true, seedDate },
                    { "Malawi", true, seedDate },
                    { "Malaysia", true, seedDate },
                    { "Maldives", true, seedDate },
                    { "Mali", true, seedDate },
                    { "Malta", true, seedDate },
                    { "Marshall Islands", true, seedDate },
                    { "Mauritania", true, seedDate },
                    { "Mauritius", true, seedDate },
                    { "Mexico", true, seedDate },
                    { "Micronesia", true, seedDate },
                    { "Moldova", true, seedDate },
                    { "Monaco", true, seedDate },
                    { "Mongolia", true, seedDate },
                    { "Montenegro", true, seedDate },
                    { "Morocco", true, seedDate },
                    { "Mozambique", true, seedDate },
                    { "Myanmar", true, seedDate },
                    { "Namibia", true, seedDate },
                    { "Nauru", true, seedDate },
                    { "Nepal", true, seedDate },
                    { "Netherlands", true, seedDate },
                    { "New Zealand", true, seedDate },
                    { "Nicaragua", true, seedDate },
                    { "Niger", true, seedDate },
                    { "Nigeria", true, seedDate },
                    { "North Korea", true, seedDate },
                    { "North Macedonia", true, seedDate },
                    { "Norway", true, seedDate },
                    { "Oman", true, seedDate },
                    { "Pakistan", true, seedDate },
                    { "Palau", true, seedDate },
                    { "Palestine", true, seedDate },
                    { "Panama", true, seedDate },
                    { "Papua New Guinea", true, seedDate },
                    { "Paraguay", true, seedDate },
                    { "Peru", true, seedDate },
                    { "Philippines", true, seedDate },
                    { "Poland", true, seedDate },
                    { "Portugal", true, seedDate },
                    { "Qatar", true, seedDate },
                    { "Romania", true, seedDate },
                    { "Russia", true, seedDate },
                    { "Rwanda", true, seedDate },
                    { "Saint Kitts and Nevis", true, seedDate },
                    { "Saint Lucia", true, seedDate },
                    { "Saint Vincent and the Grenadines", true, seedDate },
                    { "Samoa", true, seedDate },
                    { "San Marino", true, seedDate },
                    { "Sao Tome and Principe", true, seedDate },
                    { "Saudi Arabia", true, seedDate },
                    { "Senegal", true, seedDate },
                    { "Serbia", true, seedDate },
                    { "Seychelles", true, seedDate },
                    { "Sierra Leone", true, seedDate },
                    { "Singapore", true, seedDate },
                    { "Slovakia", true, seedDate },
                    { "Slovenia", true, seedDate },
                    { "Solomon Islands", true, seedDate },
                    { "Somalia", true, seedDate },
                    { "South Africa", true, seedDate },
                    { "South Korea", true, seedDate },
                    { "South Sudan", true, seedDate },
                    { "Spain", true, seedDate },
                    { "Sri Lanka", true, seedDate },
                    { "Sudan", true, seedDate },
                    { "Suriname", true, seedDate },
                    { "Sweden", true, seedDate },
                    { "Switzerland", true, seedDate },
                    { "Syria", true, seedDate },
                    { "Taiwan", true, seedDate },
                    { "Tajikistan", true, seedDate },
                    { "Tanzania", true, seedDate },
                    { "Thailand", true, seedDate },
                    { "Timor-Leste", true, seedDate },
                    { "Togo", true, seedDate },
                    { "Tonga", true, seedDate },
                    { "Trinidad and Tobago", true, seedDate },
                    { "Tunisia", true, seedDate },
                    { "Turkey", true, seedDate },
                    { "Turkmenistan", true, seedDate },
                    { "Tuvalu", true, seedDate },
                    { "Uganda", true, seedDate },
                    { "Ukraine", true, seedDate },
                    { "United Arab Emirates", true, seedDate },
                    { "United Kingdom", true, seedDate },
                    { "United States", true, seedDate },
                    { "Uruguay", true, seedDate },
                    { "Uzbekistan", true, seedDate },
                    { "Vanuatu", true, seedDate },
                    { "Vatican City", true, seedDate },
                    { "Venezuela", true, seedDate },
                    { "Vietnam", true, seedDate },
                    { "Yemen", true, seedDate },
                    { "Zambia", true, seedDate },
                    { "Zimbabwe", true, seedDate }
                });

            migrationBuilder.AddColumn<int>(
                name: "OriginCountryId",
                schema: "dbo",
                table: "Companies",
                type: "int",
                nullable: true);

            // Match any existing free-text CountryOfOrigin values against the new seeded list
            // and carry them over. Wrapped in EXEC(N'...') because OriginCountryId was just added
            // to the (existing) Companies table above -- referencing it directly here, in the same
            // script batch, would fail to compile with "Invalid column name" since SQL Server
            // resolves column names in DML statements against pre-batch schema.
            migrationBuilder.Sql(@"
                EXEC(N'
                    UPDATE c
                    SET c.OriginCountryId = oc.Id
                    FROM dbo.Companies c
                    INNER JOIN dbo.OriginCountries oc ON oc.Name = c.CountryOfOrigin
                    WHERE c.CountryOfOrigin IS NOT NULL;
                ');
            ");

            migrationBuilder.DropColumn(
                name: "CountryOfOrigin",
                schema: "dbo",
                table: "Companies");

            migrationBuilder.CreateIndex(
                name: "IX_Companies_OriginCountryId",
                schema: "dbo",
                table: "Companies",
                column: "OriginCountryId");

            migrationBuilder.AddForeignKey(
                name: "FK_Companies_OriginCountries_OriginCountryId",
                schema: "dbo",
                table: "Companies",
                column: "OriginCountryId",
                principalSchema: "dbo",
                principalTable: "OriginCountries",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Companies_OriginCountries_OriginCountryId",
                schema: "dbo",
                table: "Companies");

            migrationBuilder.DropTable(
                name: "OriginCountries",
                schema: "dbo");

            migrationBuilder.DropIndex(
                name: "IX_Companies_OriginCountryId",
                schema: "dbo",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "OriginCountryId",
                schema: "dbo",
                table: "Companies");

            migrationBuilder.AddColumn<string>(
                name: "CountryOfOrigin",
                schema: "dbo",
                table: "Companies",
                type: "nvarchar(150)",
                maxLength: 150,
                nullable: true);
        }
    }
}
