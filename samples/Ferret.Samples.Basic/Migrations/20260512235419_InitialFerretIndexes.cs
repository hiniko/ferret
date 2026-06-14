using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ferret.Example.Basic.Migrations
{
    /// <inheritdoc />
    public partial class InitialFerretIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {

            migrationBuilder.Sql("""
                CREATE EXTENSION IF NOT EXISTS "pg_trgm";
            """);

            migrationBuilder.CreateTable(
                name: "products",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    sku = table.Column<string>(type: "text", nullable: false),
                    category = table.Column<string>(type: "text", nullable: false),
                    price = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    stock = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_products", x => x.id);
                });

            migrationBuilder.Sql("""
                CREATE INDEX CONCURRENTLY IF NOT EXISTS "ix_products_name_gist_trgm" ON "products" USING gist (("name"::text) gist_trgm_ops);
            """, suppressTransaction: true);

            migrationBuilder.Sql("""
                CREATE INDEX CONCURRENTLY IF NOT EXISTS "ix_products_sku_gist_trgm" ON "products" USING gist (("sku"::text) gist_trgm_ops);
            """, suppressTransaction: true);

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

            migrationBuilder.Sql("""
                DROP INDEX CONCURRENTLY IF EXISTS "ix_products_name_gist_trgm";
            """, suppressTransaction: true);

            migrationBuilder.Sql("""
                DROP INDEX CONCURRENTLY IF EXISTS "ix_products_sku_gist_trgm";
            """, suppressTransaction: true);

            migrationBuilder.DropTable(
                name: "products");
        }
    }
}
