using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SaleOrd.Migrations
{
    /// <inheritdoc />
    public partial class AddPartyApprovalWorkflow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // defaultValue: 1 (Approved), NOT 0 (Pending) — this column is
            // being added to a table already full of real Tally-synced
            // ledgers that were never meant to be gated. SQL Server backfills
            // every existing row with this default, so defaultValue: 0 here
            // would silently flip every pre-existing party to Pending and
            // vanish them from SaleOrderController.GetParties' Approved-only
            // filter the moment this migration runs. Only brand-new parties
            // created from Masters > Parties explicitly pass Pending in the
            // INSERT itself (MastersController.CreateParty) — this default
            // never applies to them.
            migrationBuilder.AddColumn<int>(
                name: "ApprovalStatus",
                table: "Ledgers",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<string>(
                name: "FSSAIDocumentPath",
                table: "Ledgers",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RejectionReason",
                table: "Ledgers",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReviewedAt",
                table: "Ledgers",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReviewedById",
                table: "Ledgers",
                type: "nvarchar(450)",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Ledgers_ReviewedById",
                table: "Ledgers",
                column: "ReviewedById");

            migrationBuilder.AddForeignKey(
                name: "FK_Ledgers_AspNetUsers_ReviewedById",
                table: "Ledgers",
                column: "ReviewedById",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Ledgers_AspNetUsers_ReviewedById",
                table: "Ledgers");

            migrationBuilder.DropIndex(
                name: "IX_Ledgers_ReviewedById",
                table: "Ledgers");

            migrationBuilder.DropColumn(
                name: "ApprovalStatus",
                table: "Ledgers");

            migrationBuilder.DropColumn(
                name: "FSSAIDocumentPath",
                table: "Ledgers");

            migrationBuilder.DropColumn(
                name: "RejectionReason",
                table: "Ledgers");

            migrationBuilder.DropColumn(
                name: "ReviewedAt",
                table: "Ledgers");

            migrationBuilder.DropColumn(
                name: "ReviewedById",
                table: "Ledgers");
        }
    }
}
