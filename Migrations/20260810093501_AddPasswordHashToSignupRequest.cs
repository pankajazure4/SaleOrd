using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SaleOrd.Migrations
{
    /// <inheritdoc />
    public partial class AddPasswordHashToSignupRequest : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PasswordHash",
                table: "SignupRequests",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PasswordHash",
                table: "SignupRequests");
        }
    }
}
