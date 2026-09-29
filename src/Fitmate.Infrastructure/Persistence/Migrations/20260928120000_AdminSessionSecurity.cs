using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Fitmate.Infrastructure.Persistence.Migrations;

[DbContext(typeof(FitmateDbContext))]
[Migration("20260928120000_AdminSessionSecurity")]
public sealed class AdminSessionSecurity : Migration
{
    protected override void Up(MigrationBuilder b)
    {
        b.Sql("""
            ALTER TABLE refresh_tokens ADD COLUMN "SessionId" uuid NULL;
            CREATE TABLE auth_sessions (
                "Id" uuid PRIMARY KEY, "UserId" uuid NOT NULL,
                "SecurityStamp" varchar(256) NOT NULL, "AuthenticationMethod" varchar(16) NOT NULL,
                "CreatedAt" timestamptz NOT NULL, "ExpiresAt" timestamptz NOT NULL,
                "RevokedAt" timestamptz NULL, "MfaVerifiedAt" timestamptz NULL
            );
            CREATE INDEX "IX_auth_sessions_UserId_RevokedAt" ON auth_sessions ("UserId", "RevokedAt");
            CREATE TABLE admin_login_challenges (
                "TokenHash" varchar(64) PRIMARY KEY, "UserId" uuid NOT NULL,
                "SecurityStamp" varchar(256) NOT NULL, "ExpiresAt" timestamptz NOT NULL,
                "Attempts" integer NOT NULL, "Consumed" boolean NOT NULL
            );
            CREATE INDEX "IX_admin_login_challenges_ExpiresAt" ON admin_login_challenges ("ExpiresAt");
            """);
    }
    protected override void Down(MigrationBuilder b)
    {
        b.Sql("""
            DROP TABLE admin_login_challenges;
            DROP TABLE auth_sessions;
            ALTER TABLE refresh_tokens DROP COLUMN "SessionId";
            """);
    }
}
