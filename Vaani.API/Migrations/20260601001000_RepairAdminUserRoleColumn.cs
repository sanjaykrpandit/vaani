using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Vaani.API.Data;

#nullable disable

namespace Vaani.API.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(VaaniDbContext))]
    [Migration("20260601001000_RepairAdminUserRoleColumn")]
    public partial class RepairAdminUserRoleColumn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM information_schema.columns
        WHERE table_schema = 'public'
          AND table_name = 'admin_users'
          AND column_name = 'role'
    ) THEN
        ALTER TABLE admin_users
        ADD COLUMN role character varying(20) NOT NULL DEFAULT 'admin';
    END IF;
END $$;");

            migrationBuilder.Sql("UPDATE admin_users SET role = 'admin' WHERE role IS NULL OR role = '';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
DO $$
BEGIN
    IF EXISTS (
        SELECT 1
        FROM information_schema.columns
        WHERE table_schema = 'public'
          AND table_name = 'admin_users'
          AND column_name = 'role'
    ) THEN
        ALTER TABLE admin_users DROP COLUMN role;
    END IF;
END $$;");
        }
    }
}
