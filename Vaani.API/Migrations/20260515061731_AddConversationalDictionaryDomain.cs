using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vaani.API.Migrations
{
    /// <inheritdoc />
    public partial class AddConversationalDictionaryDomain : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "domain",
                table: "conversational_dictionary",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "general");

            migrationBuilder.CreateIndex(
                name: "IX_conversational_dictionary_language_code_domain_is_active",
                table: "conversational_dictionary",
                columns: new[] { "language_code", "domain", "is_active" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_conversational_dictionary_language_code_domain_is_active",
                table: "conversational_dictionary");

            migrationBuilder.DropColumn(
                name: "domain",
                table: "conversational_dictionary");
        }
    }
}
