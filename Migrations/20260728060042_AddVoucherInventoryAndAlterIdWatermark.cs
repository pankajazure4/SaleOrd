using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SaleOrd.Migrations
{
    /// <inheritdoc />
    public partial class AddVoucherInventoryAndAlterIdWatermark : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "LastVoucherAlterId",
                table: "Companies",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "VoucherInventoryEntries",
                columns: table => new
                {
                    VoucherInventoryEntryId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CompanyId = table.Column<int>(type: "int", nullable: false),
                    VoucherGUID = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    VoucherNumber = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    VoucherTypeName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    VoucherDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    AlterId = table.Column<long>(type: "bigint", nullable: false),
                    PartyLedgerName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    LedgerId = table.Column<int>(type: "int", nullable: true),
                    StockItemName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    StockItemId = table.Column<int>(type: "int", nullable: true),
                    ActualQty = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    BilledQty = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    Rate = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Discount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    GodownName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    LastSyncedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VoucherInventoryEntries", x => x.VoucherInventoryEntryId);
                    table.ForeignKey(
                        name: "FK_VoucherInventoryEntries_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "CompanyId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_VoucherInventoryEntries_Ledgers_LedgerId",
                        column: x => x.LedgerId,
                        principalTable: "Ledgers",
                        principalColumn: "LedgerId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_VoucherInventoryEntries_StockItems_StockItemId",
                        column: x => x.StockItemId,
                        principalTable: "StockItems",
                        principalColumn: "StockItemId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_VoucherInventoryEntries_CompanyId_LedgerId_StockItemId_VoucherDate",
                table: "VoucherInventoryEntries",
                columns: new[] { "CompanyId", "LedgerId", "StockItemId", "VoucherDate" });

            migrationBuilder.CreateIndex(
                name: "IX_VoucherInventoryEntries_CompanyId_VoucherGUID",
                table: "VoucherInventoryEntries",
                columns: new[] { "CompanyId", "VoucherGUID" });

            migrationBuilder.CreateIndex(
                name: "IX_VoucherInventoryEntries_LedgerId",
                table: "VoucherInventoryEntries",
                column: "LedgerId");

            migrationBuilder.CreateIndex(
                name: "IX_VoucherInventoryEntries_StockItemId",
                table: "VoucherInventoryEntries",
                column: "StockItemId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "VoucherInventoryEntries");

            migrationBuilder.DropColumn(
                name: "LastVoucherAlterId",
                table: "Companies");
        }
    }
}
