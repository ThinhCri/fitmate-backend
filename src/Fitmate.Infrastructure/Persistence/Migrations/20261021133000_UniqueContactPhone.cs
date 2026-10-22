using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fitmate.Infrastructure.Persistence.Migrations;

[DbContext(typeof(FitmateDbContext))]
[Migration("20261021133000_UniqueContactPhone")]
public sealed class UniqueContactPhone : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Canonicalize only clearly valid Vietnamese mobile numbers. Invalid
        // legacy values need operator review; never guess a phone owner.
        migrationBuilder.Sql("""
            UPDATE user_profiles SET "Phone" = CASE
              WHEN "Phone" ~ '^[+]84[35789][0-9]{8}$' THEN '0' || substring("Phone" from 4)
              WHEN "Phone" ~ '^84[35789][0-9]{8}$' THEN '0' || substring("Phone" from 3)
              ELSE btrim("Phone") END
            WHERE "Phone" IS NOT NULL;
            UPDATE user_profiles SET "Phone" = NULL WHERE "Phone" = '';
            DO $$ BEGIN
              IF EXISTS (SELECT 1 FROM user_profiles WHERE "Phone" IS NOT NULL
                         GROUP BY "Phone" HAVING count(*) > 1) THEN
                RAISE EXCEPTION 'Contact phone migration requires manual duplicate reconciliation';
              END IF;
            END $$;
            """);
        migrationBuilder.CreateIndex("IX_user_profiles_Phone", "user_profiles", "Phone", unique: true,
            filter: "\"Phone\" IS NOT NULL");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex("IX_user_profiles_Phone", "user_profiles");
        // Formatting normalization cannot be reversed safely.
    }
}
