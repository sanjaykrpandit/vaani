using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vaani.API.Migrations
{
    /// <inheritdoc />
    public partial class add_default_meeting_lang : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "meeting_language",
                table: "meetings",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "meeting_language",
                table: "meetings");
        }
    }
}
