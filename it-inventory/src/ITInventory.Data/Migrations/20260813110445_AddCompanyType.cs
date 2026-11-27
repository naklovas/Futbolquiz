using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ITInventory.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCompanyType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CompanyType",
                schema: "dbo",
                table: "Companies",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "OtherTypeDescription",
                schema: "dbo",
                table: "Companies",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CompanyType",
                schema: "dbo",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "OtherTypeDescription",
                schema: "dbo",
                table: "Companies");
        }
    }
}
