using Fitmate.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fitmate.Infrastructure.Persistence.Migrations;

[DbContext(typeof(FitmateDbContext))]
[Migration("20261011050000_AddGooglePlayAiSubscriptions")]
public sealed class AddGooglePlayAiSubscriptions : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "ai_subscriptions",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                UserId = table.Column<Guid>(type: "uuid", nullable: false),
                PurchaseTokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                ProtectedPurchaseToken = table.Column<string>(type: "text", nullable: false),
                ProductId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                BasePlanId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                State = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                VerifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_ai_subscriptions", x => x.Id));
        migrationBuilder.CreateIndex("IX_ai_subscriptions_PurchaseTokenHash", "ai_subscriptions",
            "PurchaseTokenHash", unique: true);
        migrationBuilder.CreateIndex("IX_ai_subscriptions_UserId_ExpiresAtUtc", "ai_subscriptions",
            new[] { "UserId", "ExpiresAtUtc" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DO $$ BEGIN
                IF EXISTS (SELECT 1 FROM ai_subscriptions) THEN
                    RAISE EXCEPTION 'Cannot discard Google Play subscription ownership records';
                END IF;
            END $$;
            """);
        migrationBuilder.DropTable("ai_subscriptions");
    }
}
