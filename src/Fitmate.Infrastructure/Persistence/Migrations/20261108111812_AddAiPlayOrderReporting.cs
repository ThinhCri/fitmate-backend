using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fitmate.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAiPlayOrderReporting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsTestPurchase",
                table: "ai_subscriptions",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LatestOrderId",
                table: "ai_subscriptions",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "OrderSyncAtUtc",
                table: "ai_subscriptions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ai_play_orders",
                columns: table => new
                {
                    OrderId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    SubscriptionId = table.Column<Guid>(type: "uuid", nullable: false),
                    State = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PurchasedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    TotalVnd = table.Column<long>(type: "bigint", nullable: false),
                    TaxVnd = table.Column<long>(type: "bigint", nullable: true),
                    DeveloperRevenueVnd = table.Column<long>(type: "bigint", nullable: true),
                    SyncedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ai_play_orders", x => x.OrderId);
                    table.ForeignKey(
                        name: "FK_ai_play_orders_ai_subscriptions_SubscriptionId",
                        column: x => x.SubscriptionId,
                        principalTable: "ai_subscriptions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ai_play_orders_PurchasedAtUtc",
                table: "ai_play_orders",
                column: "PurchasedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_ai_play_orders_SubscriptionId",
                table: "ai_play_orders",
                column: "SubscriptionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$ BEGIN
                  IF EXISTS (SELECT 1 FROM ai_play_orders) THEN
                    RAISE EXCEPTION 'Back up AI order records before removing reporting schema';
                  END IF;
                END $$;
                """);
            migrationBuilder.DropTable(
                name: "ai_play_orders");

            migrationBuilder.DropColumn(
                name: "IsTestPurchase",
                table: "ai_subscriptions");

            migrationBuilder.DropColumn(
                name: "LatestOrderId",
                table: "ai_subscriptions");

            migrationBuilder.DropColumn(
                name: "OrderSyncAtUtc",
                table: "ai_subscriptions");

        }
    }
}
