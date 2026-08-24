using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SaleOrd.Migrations
{
    /// <inheritdoc />
    public partial class AddOutletNameToSignupRequest : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "OutletName",
                table: "SignupRequests",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "OutletName",
                table: "SignupRequests");
        }
    }
}
