using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Vaani.API.Migrations
{
    /// <inheritdoc />
    public partial class AddConversationalDictionary : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "conversational_dictionary",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    language_code = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    formal_text = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    conversational_text = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    match_mode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "Contains"),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    updated_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_conversational_dictionary", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_conversational_dictionary_language_code",
                table: "conversational_dictionary",
                column: "language_code");

            migrationBuilder.CreateIndex(
                name: "IX_conversational_dictionary_language_code_is_active",
                table: "conversational_dictionary",
                columns: new[] { "language_code", "is_active" });

            // Seed starter conversational entries for hi-IN
            migrationBuilder.InsertData(
                table: "conversational_dictionary",
                columns: ["language_code", "formal_text", "conversational_text", "match_mode", "is_active", "created_by", "updated_by"],
                values: new object[,]
                {
                    { "hi-IN", "कृपया",         "प्लीज़",             "Contains", true, "system", "system" },
                    { "hi-IN", "धन्यवाद",        "थैंक्स",             "Contains", true, "system", "system" },
                    { "hi-IN", "क्या आप",        "क्या तुम",           "Contains", true, "system", "system" },
                    { "hi-IN", "आप किस प्रकार", "आप कैसे",            "Contains", true, "system", "system" },
                    { "hi-IN", "मैं आपसे",       "मैं तुमसे",          "Contains", true, "system", "system" },
                    { "hi-IN", "आपका स्वागत है", "वेलकम",             "Contains", true, "system", "system" },
                    { "hi-IN", "निश्चित रूप से", "बिल्कुल",           "Contains", true, "system", "system" },
                    { "hi-IN", "ठीक है",         "ओके",               "Exact",    true, "system", "system" },
                    // Seed starter conversational entries for mr-IN
                    { "mr-IN", "कृपया",          "प्लीज",             "Contains", true, "system", "system" },
                    { "mr-IN", "धन्यवाद",         "थॅंक्स",            "Contains", true, "system", "system" },
                    { "mr-IN", "आपण",             "तुम्ही",            "Contains", true, "system", "system" },
                    { "mr-IN", "आपले स्वागत आहे", "वेलकम",            "Contains", true, "system", "system" },
                    { "mr-IN", "निश्चितच",        "नक्कीच",            "Contains", true, "system", "system" },
                    { "mr-IN", "ठीक आहे",         "ओके",              "Exact",    true, "system", "system" },
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "conversational_dictionary");
        }
    }
}
