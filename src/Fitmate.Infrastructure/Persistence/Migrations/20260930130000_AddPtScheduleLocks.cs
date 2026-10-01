using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Fitmate.Infrastructure.Persistence.Migrations;

[DbContext(typeof(FitmateDbContext))]
[Migration("20260930130000_AddPtScheduleLocks")]
public sealed class AddPtScheduleLocks : Migration
{
    protected override void Up(MigrationBuilder b)
    {
        b.Sql("""
            CREATE TABLE IF NOT EXISTS pt_schedule_locks (
                pt_user_id uuid NOT NULL,
                locked_at_utc timestamptz NOT NULL,
                CONSTRAINT pk_pt_schedule_locks PRIMARY KEY (pt_user_id),
                CONSTRAINT fk_pt_schedule_locks_users_pt_user_id FOREIGN KEY (pt_user_id) REFERENCES asp_net_users ("Id") ON DELETE CASCADE
            );
            """);
    }

    protected override void Down(MigrationBuilder b)
    {
        b.Sql("""
            DROP TABLE IF EXISTS pt_schedule_locks;
            """);
    }
}
