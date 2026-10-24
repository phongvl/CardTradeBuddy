using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CardTradeBuddy.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDisclaimerAcceptance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "DisclaimerAcceptedAt",
                table: "user_profiles",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DisclaimerVersion",
                table: "user_profiles",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DisclaimerAcceptedAt",
                table: "user_profiles");

            migrationBuilder.DropColumn(
                name: "DisclaimerVersion",
                table: "user_profiles");
        }
    }
}
