using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CardTradeBuddy.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCardLanguage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Language",
                table: "cards",
                type: "character varying(8)",
                maxLength: 8,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Language",
                table: "cards");
        }
    }
}
