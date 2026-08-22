using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ferret.Example.ExistingApi.Migrations
{
    /// <inheritdoc />
    public partial class InitialWithFerretIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {

            migrationBuilder.Sql("""
                CREATE EXTENSION IF NOT EXISTS "pg_trgm";
            """);

            migrationBuilder.CreateTable(
                name: "organisations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_organisations", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "tickets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    subject = table.Column<string>(type: "text", nullable: false),
                    body = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    organisation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tickets", x => x.id);
                    table.ForeignKey(
                        name: "FK_tickets_organisations_organisation_id",
                        column: x => x.organisation_id,
                        principalTable: "organisations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_tickets_organisation_id",
                table: "tickets",
                column: "organisation_id");

            migrationBuilder.Sql("""
                CREATE INDEX CONCURRENTLY IF NOT EXISTS "ix_tickets_body_gist_trgm" ON "tickets" USING gist (("body"::text) gist_trgm_ops);
            """, suppressTransaction: true);

            migrationBuilder.Sql("""
                CREATE INDEX CONCURRENTLY IF NOT EXISTS "ix_tickets_subject_gist_trgm" ON "tickets" USING gist (("subject"::text) gist_trgm_ops);
            """, suppressTransaction: true);

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

            migrationBuilder.Sql("""
                DROP INDEX CONCURRENTLY IF EXISTS "ix_tickets_body_gist_trgm";
            """, suppressTransaction: true);

            migrationBuilder.Sql("""
                DROP INDEX CONCURRENTLY IF EXISTS "ix_tickets_subject_gist_trgm";
            """, suppressTransaction: true);

            migrationBuilder.DropTable(
                name: "tickets");

            migrationBuilder.DropTable(
                name: "organisations");
        }
    }
}
