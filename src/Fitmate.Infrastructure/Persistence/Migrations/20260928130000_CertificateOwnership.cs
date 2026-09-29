using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Fitmate.Infrastructure.Persistence.Migrations;

[DbContext(typeof(FitmateDbContext))]
[Migration("20260928130000_CertificateOwnership")]
public sealed class CertificateOwnership : Migration
{
    protected override void Up(MigrationBuilder b)
    {
        // Add the column WITHOUT backfilling legacy rows.
        // Per docs/backend-release/admin-web-finalization-handoff.md we cannot infer
        // ownership for pre-migration certificates, so we leave ImageUploadedByUserId
        // null. The application-layer ownership check (AdminCertificatesController.Preview)
        // will refuse to issue a signed URL until a fresh upload records the real uploader.
        b.Sql("""
            ALTER TABLE certificates ADD COLUMN "ImageUploadedByUserId" uuid NULL;
            CREATE INDEX "IX_certificates_ImageUploadedByUserId" ON certificates ("ImageUploadedByUserId");
            """);
    }

    protected override void Down(MigrationBuilder b)
    {
        b.Sql("""
            DROP INDEX IF EXISTS "IX_certificates_ImageUploadedByUserId";
            ALTER TABLE certificates DROP COLUMN "ImageUploadedByUserId";
            """);
    }
}
