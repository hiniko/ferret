using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ferret.Example.ExistingApi.Migrations
{
    /// <inheritdoc />
    public partial class AddFullTextSearch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {

            migrationBuilder.Sql("""
                DROP INDEX CONCURRENTLY IF EXISTS "ix_tickets_body_gist_trgm";
            """, suppressTransaction: true);

            migrationBuilder.Sql("""
                DROP INDEX CONCURRENTLY IF EXISTS "ix_tickets_subject_gist_trgm";
            """, suppressTransaction: true);

            migrationBuilder.Sql("""
                CREATE TABLE IF NOT EXISTS "tickets_search" (
                    "id" uuid PRIMARY KEY REFERENCES "tickets" ("id") ON DELETE CASCADE,
                    "updated_at" timestamptz NOT NULL DEFAULT now()
                );

            """);


            migrationBuilder.Sql("""
                CREATE INDEX CONCURRENTLY IF NOT EXISTS "ix_organisations_name_gist_trgm" ON "organisations" USING gist (("name"::text) gist_trgm_ops);
            """, suppressTransaction: true);

            migrationBuilder.Sql("""
                ALTER TABLE "tickets_search" ADD COLUMN IF NOT EXISTS "content_tsv" tsvector;

            """);

            migrationBuilder.Sql("""
                CREATE INDEX IF NOT EXISTS "ix_tickets_search_content_tsv_gin" ON "tickets_search" USING gin ("content_tsv");

            """);

            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION "tickets_search_sync"() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    INSERT INTO "tickets_search" ("id", "content_tsv", "updated_at")
                    VALUES (NEW."id", setweight(to_tsvector('english', coalesce(NEW."subject", '')), 'A') || setweight(to_tsvector('english', coalesce(NEW."body", '')), 'B'), now())
                    ON CONFLICT ("id") DO UPDATE SET
                        "content_tsv" = EXCLUDED."content_tsv",
                        "updated_at" = now();
                    RETURN NEW;
                END;
                $$;
                CREATE TRIGGER "tickets_search_sync_t"
                AFTER INSERT OR UPDATE OF "body", "subject"
                ON "tickets"
                FOR EACH ROW EXECUTE FUNCTION
                "tickets_search_sync"();

            """);

            migrationBuilder.Sql("""
                INSERT INTO "tickets_search" ("id", "content_tsv", "updated_at")
                SELECT "id", setweight(to_tsvector('english', coalesce("subject", '')), 'A') || setweight(to_tsvector('english', coalesce("body", '')), 'B'), now()
                FROM "tickets"
                ON CONFLICT ("id") DO UPDATE SET
                    "content_tsv" = EXCLUDED."content_tsv",
                    "updated_at" = now();

            """);

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

            migrationBuilder.Sql("""
                DROP INDEX CONCURRENTLY IF EXISTS "ix_organisations_name_gist_trgm";
            """, suppressTransaction: true);

            migrationBuilder.Sql("""
                DROP INDEX IF EXISTS "ix_tickets_search_content_tsv_gin";

            """);

            migrationBuilder.Sql("""
                ALTER TABLE "tickets_search" DROP COLUMN IF EXISTS "content_tsv";

            """);

            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS "tickets_search_sync_t" ON "tickets";
                DROP FUNCTION IF EXISTS "tickets_search_sync"();

            """);


            migrationBuilder.Sql("""
                CREATE INDEX CONCURRENTLY IF NOT EXISTS "ix_tickets_body_gist_trgm" ON "tickets" USING gist (("body"::text) gist_trgm_ops);
            """, suppressTransaction: true);

            migrationBuilder.Sql("""
                CREATE INDEX CONCURRENTLY IF NOT EXISTS "ix_tickets_subject_gist_trgm" ON "tickets" USING gist (("subject"::text) gist_trgm_ops);
            """, suppressTransaction: true);

        }
    }
}
