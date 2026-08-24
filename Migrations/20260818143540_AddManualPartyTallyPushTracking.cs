using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SaleOrd.Migrations
{
    /// <inheritdoc />
    public partial class AddManualPartyTallyPushTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsManuallyCreated",
                table: "Ledgers",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "TallyPushedAt",
                table: "Ledgers",
                type: "datetime2",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsManuallyCreated",
                table: "Ledgers");

            migrationBuilder.DropColumn(
                name: "TallyPushedAt",
                table: "Ledgers");
        }
    }
}
