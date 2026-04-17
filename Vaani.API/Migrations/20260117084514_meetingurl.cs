using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vaani.API.Migrations
{
    /// <inheritdoc />
    public partial class meetingurl : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "public_token",
                table: "meetings",
                type: "character varying(255)",
                maxLength: 255,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "public_token",
                table: "meetings");
        }
    }
}
