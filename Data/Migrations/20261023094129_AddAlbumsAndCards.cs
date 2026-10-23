using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CardTradeBuddy.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAlbumsAndCards : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "albums",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    IsDefault = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_albums", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "cards",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AlbumId = table.Column<Guid>(type: "uuid", nullable: false),
                    AddedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CardId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    LocalId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Category = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Rarity = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Illustrator = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Description = table.Column<string>(type: "text", nullable: true),
                    SetId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    SetName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    SetOfficialCardCount = table.Column<int>(type: "integer", nullable: true),
                    ImageUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    SetLogoUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Hp = table.Column<int>(type: "integer", nullable: true),
                    Stage = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Types = table.Column<string[]>(type: "text[]", nullable: false),
                    Retreat = table.Column<int>(type: "integer", nullable: true),
                    Finish = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    FinishAssumed = table.Column<bool>(type: "boolean", nullable: false),
                    EstimatedPriceEur = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    Pricing = table.Column<string>(type: "jsonb", nullable: true),
                    PricesCheckedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cards", x => x.Id);
                    table.ForeignKey(
                        name: "FK_cards_albums_AlbumId",
                        column: x => x.AlbumId,
                        principalTable: "albums",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_albums_UserId",
                table: "albums",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_albums_UserId_default",
                table: "albums",
                column: "UserId",
                unique: true,
                filter: "\"IsDefault\" = true");

            migrationBuilder.CreateIndex(
                name: "IX_cards_AlbumId",
                table: "cards",
                column: "AlbumId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "cards");

            migrationBuilder.DropTable(
                name: "albums");
        }
    }
}
