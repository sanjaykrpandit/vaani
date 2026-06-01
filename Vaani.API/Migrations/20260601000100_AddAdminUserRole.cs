using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Vaani.API.Data;

#nullable disable

namespace Vaani.API.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(VaaniDbContext))]
    [Migration("20260601000100_AddAdminUserRole")]
    public partial class AddAdminUserRole : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "role",
                table: "admin_users",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "admin");

            migrationBuilder.Sql("UPDATE admin_users SET role = 'admin' WHERE role IS NULL OR role = '';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "role",
                table: "admin_users");
        }
    }
}
