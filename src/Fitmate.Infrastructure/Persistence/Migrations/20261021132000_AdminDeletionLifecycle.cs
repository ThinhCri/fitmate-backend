using Fitmate.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fitmate.Infrastructure.Persistence.Migrations;

[DbContext(typeof(FitmateDbContext))]
[Migration("20261021132000_AdminDeletionLifecycle")]
public sealed class AdminDeletionLifecycle : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTime>(
            name: "DeletionRequestedAt", table: "asp_net_users", type: "timestamp with time zone", nullable: true);

        // Existing PendingDeletion rows were started by the legacy self-service flow.
        migrationBuilder.Sql("""
            UPDATE asp_net_users SET "DeletionRequestedAt" = "DeletedAt"
            WHERE "Status" = 2 AND "DeletedAt" IS NOT NULL AND "DeletionRequestedAt" IS NULL;
            """);

        // The legacy partial unique indexes allowed a new account to reuse an
        // email/phone while the old one was pending deletion. Stop before
        // replacing the indexes if that happened; an operator must reconcile
        // those identities explicitly, never choose one automatically.
        migrationBuilder.Sql("""
            DO $$ BEGIN
              IF EXISTS (SELECT 1 FROM asp_net_users WHERE "Status" <> 3 AND "NormalizedEmail" IS NOT NULL
                         GROUP BY "NormalizedEmail" HAVING count(*) > 1)
                 OR EXISTS (SELECT 1 FROM asp_net_users WHERE "Status" <> 3 AND "Email" IS NOT NULL
                            GROUP BY "Email" HAVING count(*) > 1)
                 OR EXISTS (SELECT 1 FROM asp_net_users WHERE "Status" <> 3 AND "PhoneNumberConfirmed" = TRUE
                            AND "PhoneNumber" IS NOT NULL GROUP BY "PhoneNumber" HAVING count(*) > 1) THEN
                RAISE EXCEPTION 'Account-deletion migration requires manual duplicate identity reconciliation';
              END IF;
            END $$;
            """);

        migrationBuilder.DropIndex("IX_asp_net_users_Email", "asp_net_users");
        migrationBuilder.DropIndex("EmailIndex", "asp_net_users");
        migrationBuilder.DropIndex("IX_asp_net_users_PhoneNumber", "asp_net_users");
        migrationBuilder.CreateIndex("IX_asp_net_users_Email", "asp_net_users", "Email", unique: true,
            filter: "\"Status\" <> 3");
        migrationBuilder.CreateIndex("EmailIndex", "asp_net_users", "NormalizedEmail", unique: true,
            filter: "\"Status\" <> 3");
        migrationBuilder.CreateIndex("IX_asp_net_users_PhoneNumber", "asp_net_users", "PhoneNumber", unique: true,
            filter: "\"PhoneNumberConfirmed\" = TRUE AND \"PhoneNumber\" IS NOT NULL AND \"Status\" <> 3");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex("IX_asp_net_users_Email", "asp_net_users");
        migrationBuilder.DropIndex("EmailIndex", "asp_net_users");
        migrationBuilder.DropIndex("IX_asp_net_users_PhoneNumber", "asp_net_users");
        migrationBuilder.CreateIndex("IX_asp_net_users_Email", "asp_net_users", "Email", unique: true,
            filter: "\"DeletedAt\" IS NULL");
        migrationBuilder.CreateIndex("EmailIndex", "asp_net_users", "NormalizedEmail", unique: true,
            filter: "\"DeletedAt\" IS NULL");
        migrationBuilder.CreateIndex("IX_asp_net_users_PhoneNumber", "asp_net_users", "PhoneNumber", unique: true,
            filter: "\"PhoneNumberConfirmed\" = TRUE AND \"PhoneNumber\" IS NOT NULL AND \"DeletedAt\" IS NULL");
        migrationBuilder.DropColumn("DeletionRequestedAt", "asp_net_users");
    }
}
