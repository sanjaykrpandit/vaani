using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vaani.API.Migrations
{
    /// <inheritdoc />
    public partial class update_column : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "UserName",
                table: "sessions",
                newName: "user_name");

            migrationBuilder.RenameColumn(
                name: "SessionTrascript",
                table: "sessions",
                newName: "session_transcript");

            migrationBuilder.RenameColumn(
                name: "SessionLog",
                table: "sessions",
                newName: "session_log");

            migrationBuilder.RenameColumn(
                name: "CreatedAt",
                table: "sessions",
                newName: "created_at");

            migrationBuilder.RenameColumn(
                name: "UpdatedBy",
                table: "meetings",
                newName: "updated_by");

            migrationBuilder.RenameColumn(
                name: "CreatedBy",
                table: "meetings",
                newName: "created_by");

            migrationBuilder.AlterColumn<string>(
                name: "updated_by",
                table: "meetings",
                type: "character varying(255)",
                maxLength: 255,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "created_by",
                table: "meetings",
                type: "character varying(255)",
                maxLength: 255,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "user_name",
                table: "sessions",
                newName: "UserName");

            migrationBuilder.RenameColumn(
                name: "session_transcript",
                table: "sessions",
                newName: "SessionTrascript");

            migrationBuilder.RenameColumn(
                name: "session_log",
                table: "sessions",
                newName: "SessionLog");

            migrationBuilder.RenameColumn(
                name: "created_at",
                table: "sessions",
                newName: "CreatedAt");

            migrationBuilder.RenameColumn(
                name: "updated_by",
                table: "meetings",
                newName: "UpdatedBy");

            migrationBuilder.RenameColumn(
                name: "created_by",
                table: "meetings",
                newName: "CreatedBy");

            migrationBuilder.AlterColumn<string>(
                name: "UpdatedBy",
                table: "meetings",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(255)",
                oldMaxLength: 255);

            migrationBuilder.AlterColumn<string>(
                name: "CreatedBy",
                table: "meetings",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(255)",
                oldMaxLength: 255);
        }
    }
}
