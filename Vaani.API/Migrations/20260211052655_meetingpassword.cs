using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vaani.API.Migrations
{
    /// <inheritdoc />
    public partial class meetingpassword : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PasswordHash",
                table: "meetings",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PasswordSalt",
                table: "meetings",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "RequiresPassword",
                table: "meetings",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PasswordHash",
                table: "meetings");

            migrationBuilder.DropColumn(
                name: "PasswordSalt",
                table: "meetings");

            migrationBuilder.DropColumn(
                name: "RequiresPassword",
                table: "meetings");
        }
    }
}
