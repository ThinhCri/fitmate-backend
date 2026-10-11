using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fitmate.Infrastructure.Persistence.Migrations;

public partial class AddPtPayoutAccounts : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>("PayeeAccountName", "manual_pt_settlements",
            type: "character varying(160)", maxLength: 160, nullable: true);
        migrationBuilder.AddColumn<string>("PayeeAccountNumber", "manual_pt_settlements",
            type: "character varying(32)", maxLength: 32, nullable: true);
        migrationBuilder.AddColumn<string>("PayeeBankCode", "manual_pt_settlements",
            type: "character varying(20)", maxLength: 20, nullable: true);
        migrationBuilder.AddColumn<string>("PayeeBankName", "manual_pt_settlements",
            type: "character varying(120)", maxLength: 120, nullable: true);
        migrationBuilder.AddColumn<DateTime>("PayeeVerifiedAtUtc", "manual_pt_settlements",
            type: "timestamp with time zone", nullable: true);

        migrationBuilder.CreateTable(
            name: "pt_payout_accounts",
            columns: table => new
            {
                PtUserId = table.Column<Guid>(type: "uuid", nullable: false),
                BankCode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                BankName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                AccountNumber = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                AccountName = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                VerificationProvider = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                VerificationReference = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                VerifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_pt_payout_accounts", x => x.PtUserId);
                table.ForeignKey("FK_pt_payout_accounts_asp_net_users_PtUserId", x => x.PtUserId,
                    "asp_net_users", "Id", onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex("IX_pt_payout_accounts_BankCode_AccountNumber",
            "pt_payout_accounts", new[] { "BankCode", "AccountNumber" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("pt_payout_accounts");
        migrationBuilder.DropColumn("PayeeAccountName", "manual_pt_settlements");
        migrationBuilder.DropColumn("PayeeAccountNumber", "manual_pt_settlements");
        migrationBuilder.DropColumn("PayeeBankCode", "manual_pt_settlements");
        migrationBuilder.DropColumn("PayeeBankName", "manual_pt_settlements");
        migrationBuilder.DropColumn("PayeeVerifiedAtUtc", "manual_pt_settlements");
    }
}
